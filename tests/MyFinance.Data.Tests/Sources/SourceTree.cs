namespace MyFinance.Data.Tests.Sources;

/// <summary>Locates the source tree, for tests that read the repository's own files.</summary>
/// <remarks>
/// No test project references <c>MyFinance.App</c> — it targets <c>net10.0-windows</c> and the
/// build runs on Linux — so its markup is checked as XML instead, from here, where the types
/// the markup binds to are reachable for reflection. The documentation is checked from here
/// for the same reason: the pages are files in the repository, not compiled artefacts.
/// </remarks>
public static class SourceTree
{
    private static readonly Lazy<string?> Located = new(Locate);

    public static string? Root => Located.Value;

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MyFinance.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>A test that only runs where the source tree is present.</summary>
public sealed class SourceTreeFactAttribute : FactAttribute
{
    public SourceTreeFactAttribute()
    {
        if (SourceTree.Root is null)
        {
            Skip = "The source tree was not found; this test reads the interface markup.";
        }
    }
}
