using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Data.Security;

namespace MyFinance.Data.Services;

/// <summary>
/// Reads and writes the book's key/value settings.
/// </summary>
/// <remarks>
/// Settings live inside the encrypted book rather than in the registry or a config file,
/// so anything derived from the user's data is protected by the same key the data is.
/// </remarks>
public sealed class SettingsService
{
    /// <summary>Per-book secret that keys the OFX account digest.</summary>
    public const string OfxAccountKeySecret = "import.ofx.account_key_secret";

    /// <summary>
    /// The database schema version this book was last written by.
    /// </summary>
    /// <remarks>
    /// The authoritative copy. The sidecar carries a mirror so the version can be read before
    /// the book is opened at all; where the two disagree this one wins, because the sidecar
    /// can legitimately lag — a process can die between the two writes — but it cannot
    /// legitimately lead.
    /// </remarks>
    public const string SchemaVersionKey = "schema.version";

    /// <summary>Which register columns the user chose to print, comma separated.</summary>
    /// <remarks>
    /// Absent means "decide for me" — the fitter drops columns by priority until the page
    /// fits. Present means the user has said what they want, and nothing in it is ever
    /// dropped to make room. See `specs/016-printing`, FR-006.
    /// </remarks>
    public const string PrintColumnsKey = "print.columns";

    /// <summary>Remembered page setup — currently the orientation.</summary>
    public const string PrintPageSetupKey = "print.pagesetup";

    private readonly IBookContextFactory _factory;

    public SettingsService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.AppSettings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using MyFinanceDbContext db = _factory.CreateContext();

        AppSetting? existing = await db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the schema version recorded in the database, or
    /// <see cref="BookSchema.Unstamped"/> when the book predates versioning.
    /// </summary>
    internal static int ReadSchemaVersion(MyFinanceDbContext db)
    {
        string? value = db.AppSettings
            .AsNoTracking()
            .Where(s => s.Key == SchemaVersionKey)
            .Select(s => s.Value)
            .FirstOrDefault();

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
            ? version
            : BookSchema.Unstamped;
    }

    /// <summary>Stamps the database with a schema version.</summary>
    internal static void WriteSchemaVersion(MyFinanceDbContext db, int version)
    {
        string encoded = version.ToString(CultureInfo.InvariantCulture);

        AppSetting? existing = db.AppSettings.FirstOrDefault(s => s.Key == SchemaVersionKey);

        if (existing is null)
        {
            db.AppSettings.Add(new AppSetting { Key = SchemaVersionKey, Value = encoded });
        }
        else
        {
            existing.Value = encoded;
        }

        db.SaveChanges();
    }

    /// <summary>
    /// Returns the book's OFX digest secret, generating one on first use.
    /// </summary>
    /// <remarks>
    /// The secret is what makes the stored account digest safe. Account numbers carry so
    /// little entropy that a plain hash of one could be reversed by enumerating candidates
    /// in seconds, which would defeat the point of storing a digest rather than the number
    /// itself. Keyed under a random per-book secret, the digest is meaningless outside this
    /// book. Losing it therefore costs only the automatic account match on the next import,
    /// never any data.
    /// </remarks>
    public async Task<byte[]> GetOrCreateOfxSecretAsync(CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        return await GetOrCreateOfxSecretAsync(db, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Context-scoped form, so an import can obtain the secret inside its own transaction
    /// rather than opening a second connection part-way through a write.
    /// </summary>
    internal static async Task<byte[]> GetOrCreateOfxSecretAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        AppSetting? existing = await db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == OfxAccountKeySecret, cancellationToken)
            .ConfigureAwait(false);

        if (existing?.Value is string stored && stored.Length > 0)
        {
            return Convert.FromBase64String(stored);
        }

        byte[] secret = RandomNumberGenerator.GetBytes(32);
        string encoded = Convert.ToBase64String(secret);

        if (existing is null)
        {
            db.AppSettings.Add(new AppSetting { Key = OfxAccountKeySecret, Value = encoded });
        }
        else
        {
            existing.Value = encoded;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return secret;
    }
}
