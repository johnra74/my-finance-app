using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Progress;
using MyFinance.Import.Categorization;

namespace MyFinance.Data.Services;

/// <summary>
/// Keeps each payee's embedding in the book, and builds the index that uses them.
/// </summary>
/// <remarks>
/// <para>
/// The vectors are cached because producing them is slow — about twenty-five seconds for four
/// thousand payees — and almost never changes. Only payees whose name has changed, or which
/// have never been embedded, go through the model.
/// </para>
/// <para>
/// The whole thing is optional. With no embedder, or one whose model would not load, this
/// does nothing and the suggestion chain carries on with the sources it has always had.
/// </para>
/// </remarks>
public sealed class PayeeEmbeddingService
{
    /// <summary>
    /// Identifies the model behind the cached vectors.
    /// </summary>
    /// <remarks>
    /// Stored per row so that changing the model invalidates every vector rather than
    /// silently mixing two incompatible spaces, whose cosines would be meaningless.
    /// </remarks>
    public const string ModelIdentity = "all-MiniLM-L6-v2-int8";

    /// <summary>What this is called while it is happening.</summary>
    private const string Stage = "Learning your merchants";

    private readonly IBookContextFactory _factory;

    public PayeeEmbeddingService(IBookContextFactory factory) => _factory = factory;

    /// <summary>Embeds any payee that needs it, and forgets vectors from another model.</summary>
    public async Task<int> RefreshAsync(
        ITextEmbedder embedder,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embedder);

        if (!embedder.IsAvailable)
        {
            return 0;
        }

        await using MyFinanceDbContext db = _factory.CreateContext();

        // A vector from a different model cannot be compared with one from this model, so it
        // goes rather than lingering as a source of nonsense neighbours.
        await db.PayeeEmbeddings
            .Where(e => e.Model != ModelIdentity)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Payee> payees = await db.Payees
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, PayeeEmbedding> cached = await db.PayeeEmbeddings
            .ToDictionaryAsync(e => e.PayeeId, cancellationToken)
            .ConfigureAwait(false);

        List<(Payee Payee, string Hash)> stale =
        [
            .. payees
                .Select(p => (Payee: p, Hash: HashOf(TextFor(p))))
                .Where(pair => !cached.TryGetValue(pair.Payee.Id, out PayeeEmbedding? existing)
                    || !string.Equals(existing.TextHash, pair.Hash, StringComparison.Ordinal)),
        ];

        if (stale.Count == 0)
        {
            progress?.Report(new WorkProgress(Stage, 0, 0));
            return 0;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int done = 0;

        progress?.Report(new WorkProgress(Stage, 0, stale.Count));

        // In slices, so a book with thousands of new payees reports progress and can be
        // stopped part-way without losing what it has already done.
        const int Slice = 64;

        for (int start = 0; start < stale.Count; start += Slice)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<(Payee Payee, string Hash)> batch = [.. stale.Skip(start).Take(Slice)];

            IReadOnlyList<float[]> vectors = await Task
                .Run(() => embedder.Embed([.. batch.Select(b => TextFor(b.Payee))], cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            for (int i = 0; i < batch.Count && i < vectors.Count; i++)
            {
                (Payee payee, string hash) = batch[i];
                byte[] packed = VectorQuantizer.Pack(vectors[i]);

                if (cached.TryGetValue(payee.Id, out PayeeEmbedding? existing))
                {
                    existing.TextHash = hash;
                    existing.Vector = packed;
                    existing.Model = ModelIdentity;
                    existing.CreatedUtc = now;
                }
                else
                {
                    db.PayeeEmbeddings.Add(new PayeeEmbedding
                    {
                        PayeeId = payee.Id,
                        TextHash = hash,
                        Vector = packed,
                        Model = ModelIdentity,
                        CreatedUtc = now,
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            done += batch.Count;
            progress?.Report(new WorkProgress(Stage, done, stale.Count));
        }

        return done;
    }

    /// <summary>
    /// Builds the index from what is cached.
    /// </summary>
    /// <remarks>
    /// Only payees with a remembered category are included: one with no category has no vote
    /// to cast, and keeping it would only slow the search down.
    /// </remarks>
    public async Task<SimilarityIndex> LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        var rows = await db.PayeeEmbeddings
            .AsNoTracking()
            .Where(e => e.Model == ModelIdentity && e.Payee!.LastCategoryId != null)
            .Select(e => new
            {
                e.PayeeId,
                e.Payee!.Name,
                CategoryId = e.Payee.LastCategoryId!.Value,
                e.Vector,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return SimilarityIndex.Build(rows.Select(r => new SimilarityEntry(
            r.PayeeId,
            r.Name,
            r.CategoryId,
            VectorQuantizer.Unpack(r.Vector))));
    }

    /// <summary>How many payees still have no usable vector.</summary>
    public async Task<int> CountMissingAsync(CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        int payees = await db.Payees.CountAsync(p => p.IsActive, cancellationToken).ConfigureAwait(false);
        int embedded = await db.PayeeEmbeddings
            .CountAsync(e => e.Model == ModelIdentity, cancellationToken)
            .ConfigureAwait(false);

        return Math.Max(payees - embedded, 0);
    }

    /// <summary>The text a payee is recognised by.</summary>
    private static string TextFor(Payee payee) => payee.Name;

    private static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
