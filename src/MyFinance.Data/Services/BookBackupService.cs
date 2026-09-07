using System.Globalization;
using MyFinance.Core.Progress;
using MyFinance.Data.Security;

namespace MyFinance.Data.Services;

/// <summary>How this book is backed up.</summary>
/// <remarks>
/// The defaults matter more here than in most settings. There is no password recovery and no
/// copy of a book anywhere else, so the safe default is on, and the number kept is high
/// enough that a mistake noticed a fortnight later is still recoverable.
/// </remarks>
public sealed record BackupPreferences
{
    public static BackupPreferences Default { get; } = new();

    /// <summary>Whether a backup is taken automatically when the book is closed.</summary>
    public bool Automatic { get; init; } = true;

    /// <summary>How many automatic backups of this book to keep.</summary>
    public int Keep { get; init; } = 10;

    /// <summary>Where they go. Empty means "beside the book".</summary>
    public string? Directory { get; init; }

    /// <summary>Clamped to something sane, whatever a hand-edited setting says.</summary>
    public int EffectiveKeep => Math.Clamp(Keep, 1, 200);
}

/// <summary>
/// Backups as the application uses them: remembered preferences, and one call to run them.
/// </summary>
/// <remarks>
/// The mechanics of writing an archive live in <see cref="BackupService" />, which knows
/// nothing about settings. This is the layer that decides when and where, and it is separate
/// so the file handling can be tested without a book's settings table anywhere near it.
/// </remarks>
public sealed class BookBackupService
{
    public const string AutomaticKey = "backup.automatic";
    public const string KeepKey = "backup.keep";
    public const string DirectoryKey = "backup.directory";

    private readonly SettingsService _settings;

    public BookBackupService(IBookContextFactory factory) => _settings = new SettingsService(factory);

    public async Task<BackupPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        string? automatic = await _settings.GetAsync(AutomaticKey, cancellationToken).ConfigureAwait(false);
        string? keep = await _settings.GetAsync(KeepKey, cancellationToken).ConfigureAwait(false);
        string? directory = await _settings.GetAsync(DirectoryKey, cancellationToken).ConfigureAwait(false);

        return new BackupPreferences
        {
            // Absent means never set, which for a new book means the safe default.
            Automatic = automatic is null || bool.TryParse(automatic, out bool on) && on,
            Keep = int.TryParse(keep, CultureInfo.InvariantCulture, out int count)
                ? count
                : BackupPreferences.Default.Keep,
            Directory = string.IsNullOrWhiteSpace(directory) ? null : directory,
        };
    }

    public async Task SavePreferencesAsync(
        BackupPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        await _settings.SetAsync(
            AutomaticKey,
            preferences.Automatic.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);

        await _settings.SetAsync(
            KeepKey,
            preferences.EffectiveKeep.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);

        await _settings.SetAsync(
            DirectoryKey,
            string.IsNullOrWhiteSpace(preferences.Directory) ? null : preferences.Directory,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Where this book's backups are kept.</summary>
    public async Task<string> GetDirectoryAsync(Book book, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        BackupPreferences preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        return Resolve(book, preferences);
    }

    private static string Resolve(Book book, BackupPreferences preferences) =>
        preferences.Directory ?? BackupService.DefaultDirectoryFor(book.DatabasePath);

    /// <summary>Every backup of this book, newest first.</summary>
    public async Task<IReadOnlyList<BackupEntry>> ListAsync(
        Book book,
        CancellationToken cancellationToken = default)
    {
        string directory = await GetDirectoryAsync(book, cancellationToken).ConfigureAwait(false);
        return BackupService.List(directory, Path.GetFileNameWithoutExtension(book.DatabasePath));
    }

    /// <summary>
    /// Backs the book up now, to the remembered folder or to somewhere the user chose.
    /// </summary>
    public async Task<BackupEntry> BackUpNowAsync(
        Book book,
        string? destinationPath = null,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        BackupPreferences preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        string bookName = Path.GetFileNameWithoutExtension(book.DatabasePath);

        string destination = destinationPath ?? Path.Combine(
            Resolve(book, preferences),
            BackupService.SuggestFileName(bookName, DateTimeOffset.Now));

        // Writing the file is IO-bound but the compression is not; keep it off the UI thread.
        BackupEntry entry = await Task
            .Run(() => BackupService.Create(book, destination, progress, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        // Only the managed folder is pruned. A backup the user deliberately put on a memory
        // stick is theirs, and nothing here is going to start deleting from it.
        if (destinationPath is null)
        {
            BackupService.Prune(Resolve(book, preferences), bookName, preferences.EffectiveKeep);
        }

        return entry;
    }

    /// <summary>
    /// Takes the backup that happens when a book is closed, if that is switched on.
    /// </summary>
    /// <remarks>
    /// Returns null rather than throwing when it is switched off or when it fails. Closing a
    /// book must not be something that can fail: the user has already finished, and refusing
    /// to close because a memory stick was unplugged would leave them stuck. The caller
    /// reports what happened instead.
    /// </remarks>
    public async Task<BackupEntry?> BackUpOnCloseAsync(
        Book book,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        BackupPreferences preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);

        if (!preferences.Automatic)
        {
            return null;
        }

        return await BackUpNowAsync(book, null, null, cancellationToken).ConfigureAwait(false);
    }
}
