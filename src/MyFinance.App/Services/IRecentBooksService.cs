namespace MyFinance.App.Services;

/// <summary>A previously opened book offered on the welcome screen.</summary>
public sealed record RecentBook(string DatabasePath, DateTimeOffset LastOpenedUtc)
{
    public string DisplayName => Path.GetFileNameWithoutExtension(DatabasePath);

    /// <summary>False once the file has been moved or deleted.</summary>
    public bool StillExists => File.Exists(DatabasePath);
}

/// <summary>
/// Remembers which books have been opened.
/// </summary>
/// <remarks>
/// This list lives outside the encrypted book, in the user's roaming app data, because it
/// has to be readable before any book is unlocked. It holds file paths only — never
/// passwords, and nothing about the contents.
/// </remarks>
public interface IRecentBooksService
{
    IReadOnlyList<RecentBook> GetRecent();

    void Remember(string databasePath);

    void Forget(string databasePath);
}
