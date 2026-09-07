using System.Text.Json;

namespace MyFinance.App.Services;

/// <inheritdoc cref="IRecentBooksService" />
public sealed class RecentBooksService : IRecentBooksService
{
    private const int MaxEntries = 10;

    private readonly string _storePath;

    public RecentBooksService()
        : this(DefaultStorePath())
    {
    }

    internal RecentBooksService(string storePath)
    {
        _storePath = storePath;
    }

    public IReadOnlyList<RecentBook> GetRecent()
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        try
        {
            List<RecentBook>? entries =
                JsonSerializer.Deserialize<List<RecentBook>>(File.ReadAllText(_storePath));

            return entries?
                .OrderByDescending(e => e.LastOpenedUtc)
                .ToArray() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt recent list is a cosmetic problem; it must never block startup.
            return [];
        }
    }

    public void Remember(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string full = Path.GetFullPath(databasePath);

        List<RecentBook> entries = GetRecent()
            .Where(e => !PathsMatch(e.DatabasePath, full))
            .ToList();

        entries.Insert(0, new RecentBook(full, DateTimeOffset.UtcNow));

        Save(entries.Take(MaxEntries));
    }

    public void Forget(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        Save(GetRecent().Where(e => !PathsMatch(e.DatabasePath, databasePath)));
    }

    private void Save(IEnumerable<RecentBook> entries)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_storePath, JsonSerializer.Serialize(entries.ToArray()));
        }
        catch (IOException)
        {
            // Losing the recent list is not worth failing an otherwise successful open.
        }
    }

    private static bool PathsMatch(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string DefaultStorePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MyFinance",
        "recent-books.json");
}
