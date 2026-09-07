using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Primitives;
using MyFinance.Import.Categorization;

namespace MyFinance.Data.Services;

/// <summary>
/// Everything the category chain needs, gathered once.
/// </summary>
/// <remarks>
/// Held together rather than passed as four arguments because they are only ever built and
/// used together, and because caching them individually would allow a set where the classifier
/// and the similarity index disagree about which transactions exist.
/// </remarks>
/// <param name="Rules">The user's own rules, in priority order.</param>
/// <param name="Classifier">Trained on the categorized history.</param>
/// <param name="Similar">Payee vectors, empty when there is no embedder.</param>
/// <param name="MerchantCodes">Industry code to category.</param>
public sealed record SuggestionContext(
    IReadOnlyList<RuleSpec> Rules,
    CategoryClassifier Classifier,
    SimilarityIndex Similar,
    IReadOnlyDictionary<string, int> MerchantCodes)
{
    public static SuggestionContext Empty { get; } = new(
        [],
        CategoryClassifier.Empty,
        SimilarityIndex.Empty,
        new Dictionary<string, int>(StringComparer.Ordinal));
}

/// <summary>One transaction to categorize, as the caller knows it.</summary>
/// <param name="AccountId">The account it belongs to.</param>
/// <param name="PayeeName">The payee as typed or recorded.</param>
/// <param name="Amount">Signed from the account's point of view.</param>
/// <param name="Memo">Free text, which the classifier reads alongside the payee.</param>
/// <param name="MerchantCode">An industry code, when a bank supplied one.</param>
public readonly record struct SuggestionRequest(
    int AccountId,
    string? PayeeName,
    Money Amount,
    string? Memo = null,
    string? MerchantCode = null);

/// <summary>
/// Suggests a category, for the register as well as for an import.
/// </summary>
/// <remarks>
/// <para>
/// The chain itself lives in <see cref="CategorySuggester" />; what this adds is the
/// expensive part. Training the classifier reads every categorized transaction in the book —
/// sixteen thousand of them on a real one — so rebuilding it each time somebody opens the
/// transaction editor would make filing one row cost as much as importing a statement.
/// </para>
/// <para>
/// The cache is checked against a cheap probe of the book rather than invalidated by callers.
/// Threading an <c>Invalidate()</c> through the register, the importer, the migration and the
/// rule editor would be four chances to forget one, and a forgotten one leaves the editor
/// quietly recommending from a model that predates the last hundred transactions — wrong in a
/// way nobody would notice. A probe cannot be forgotten.
/// </para>
/// </remarks>
public sealed class SuggestionService
{
    private readonly IBookContextFactory _factory;
    private readonly ITextEmbedder _embedder;
    private readonly PayeeEmbeddingService _embeddings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SuggestionContext? _cached;
    private BookFingerprint _cachedFor;

    public SuggestionService(IBookContextFactory factory, ITextEmbedder? embedder = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
        _embedder = embedder ?? NullTextEmbedder.Instance;
        _embeddings = new PayeeEmbeddingService(factory);
    }

    /// <summary>
    /// What the cache is keyed on: enough of the book's shape to notice a change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counts plus the highest transaction and split identifiers. Between them these catch an
    /// added, deleted or re-categorized transaction and any change to the rules — a
    /// re-categorization rewrites the row's split, so the split identifier moves even though
    /// the counts do not.
    /// </para>
    /// <para>
    /// Identifiers rather than the newest <c>ModifiedUtc</c>, which would be the more direct
    /// question: SQLite cannot apply <c>MAX</c> to a <c>DateTimeOffset</c>, and ordering the
    /// column client-side would read every row, which is the cost this probe exists to avoid.
    /// The one case identifiers miss is an edit that changes neither counts nor splits — a
    /// corrected memo, say — and a memo does not move a suggestion.
    /// </para>
    /// </remarks>
    private readonly record struct BookFingerprint(
        int Transactions,
        int Rules,
        int Payees,
        int NewestTransactionId,
        int NewestSplitId);

    /// <summary>The context for the book as it stands, rebuilding it only if it has moved on.</summary>
    public async Task<SuggestionContext> GetContextAsync(CancellationToken cancellationToken = default)
    {
        BookFingerprint now = await FingerprintAsync(cancellationToken).ConfigureAwait(false);

        if (_cached is SuggestionContext cached && _cachedFor == now)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Checked again inside the gate: two dialogs opening together would otherwise
            // both train a classifier over the whole book.
            if (_cached is SuggestionContext current && _cachedFor == now)
            {
                return current;
            }

            SuggestionContext built = await BuildAsync(cancellationToken).ConfigureAwait(false);

            _cached = built;
            _cachedFor = now;

            return built;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Suggests a category for one transaction.</summary>
    public async Task<CategorySuggestion> SuggestAsync(
        SuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PayeeName))
        {
            return CategorySuggestion.None;
        }

        SuggestionContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);

        int? lastCategoryId = await LastCategoryForAsync(request.PayeeName, cancellationToken)
            .ConfigureAwait(false);

        float[]? vector = Embed(request, context, cancellationToken);

        return CategorySuggester.Suggest(
            new CategorySuggester.SuggestionInput(
                request.AccountId,
                request.PayeeName,
                request.Memo,
                request.Amount,

                // Nothing here comes from a file; that source belongs to the importer.
                FileCategoryId: null,
                lastCategoryId,
                vector,
                request.MerchantCode),
            context.Rules,
            context.Classifier,
            similar: context.Similar,
            merchantCodes: context.MerchantCodes);
    }

    private float[]? Embed(
        SuggestionRequest request,
        SuggestionContext context,
        CancellationToken cancellationToken)
    {
        if (!_embedder.IsAvailable || !context.Similar.IsUseful)
        {
            return null;
        }

        string text = string.Join(
            " ",
            new[] { request.PayeeName, request.Memo }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        IReadOnlyList<float[]> vectors = _embedder.Embed([text], cancellationToken);
        return vectors.Count > 0 ? vectors[0] : null;
    }

    /// <summary>What this payee was last filed under, by name.</summary>
    private async Task<int?> LastCategoryForAsync(string name, CancellationToken cancellationToken)
    {
        var payees = new PayeeService(_factory);

        PayeeListItem? payee = await payees.FindByNameAsync(name, cancellationToken)
            .ConfigureAwait(false);

        return payee?.LastCategoryId;
    }

    private async Task<BookFingerprint> FingerprintAsync(CancellationToken cancellationToken)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return new BookFingerprint(
            await db.Transactions.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.CategorizationRules.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Payees.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Transactions
                .Select(t => t.Id)
                .DefaultIfEmpty()
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false),
            await db.TransactionSplits
                .Select(sp => sp.Id)
                .DefaultIfEmpty()
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false));
    }

    private async Task<SuggestionContext> BuildAsync(CancellationToken cancellationToken)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        IReadOnlyList<RuleSpec> rules =
            await CategorizationRuleService.LoadSpecsAsync(db, cancellationToken).ConfigureAwait(false);

        CategoryClassifier classifier =
            await TrainClassifierAsync(db, cancellationToken).ConfigureAwait(false);

        SimilarityIndex similar = _embedder.IsAvailable
            ? await _embeddings.LoadIndexAsync(cancellationToken).ConfigureAwait(false)
            : SimilarityIndex.Empty;

        Dictionary<string, int> merchantCodes = await db.MerchantCodeCategories
            .AsNoTracking()
            .ToDictionaryAsync(m => m.Code, m => m.CategoryId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        return new SuggestionContext(rules, classifier, similar, merchantCodes);
    }

    /// <summary>
    /// Trains the classifier on everything the user has already filed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Split transactions are excluded because there is no single category to learn; transfers
    /// and voided rows because neither represents a spending decision, and including them
    /// would teach the model that "transfer" belongs in whatever category happened to be near it.
    /// </para>
    /// <para>
    /// This lives here rather than in the importer now that the register asks for suggestions
    /// too — two callers training their own copies over the same sixteen thousand rows is the
    /// cost this class exists to avoid.
    /// </para>
    /// </remarks>
    internal static async Task<CategoryClassifier> TrainClassifierAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => !t.IsVoid && t.TransferPeerId == null && t.Splits.Count == 1)
            .Select(t => new
            {
                PayeeName = t.Payee!.Name,
                t.Memo,
                CategoryId = t.Splits.First().CategoryId,
            })
            .Where(t => t.CategoryId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return CategoryClassifier.Train(rows.Select(r => new TrainingExample(
            string.Join(" ", new[] { r.PayeeName, r.Memo }.Where(p => !string.IsNullOrWhiteSpace(p))),
            r.CategoryId!.Value)));
    }
}
