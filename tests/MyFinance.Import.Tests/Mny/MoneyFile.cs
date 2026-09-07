namespace MyFinance.Import.Tests.Mny;

/// <summary>
/// Locates a real Money file to test against, if the developer has one.
/// </summary>
/// <remarks>
/// A `.mny` is somebody's entire financial history, so it is gitignored and never committed.
/// These tests therefore have to run against whatever file happens to be present and assert
/// only things that stay true of any book — never a balance or an account name, which would
/// put the file's contents into source control by the back door.
/// </remarks>
internal static class MoneyFile
{
    private static readonly Lazy<string?> Located = new(Locate);

    public static string? Path => Located.Value;

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            FileInfo[] found = directory.GetFiles("*.mny", SearchOption.TopDirectoryOnly);

            if (found.Length > 0)
            {
                return found[0].FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>A test that only runs when a real Money file is on the machine.</summary>
public sealed class MoneyFileFactAttribute : FactAttribute
{
    public MoneyFileFactAttribute()
    {
        if (MoneyFile.Path is null)
        {
            Skip = "No .mny file was found. Put one in the repository root to run this test.";
        }
    }
}
