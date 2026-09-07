using MyFinance.Core.Help;
using MyFinance.Data.Tests.Sources;

namespace MyFinance.Data.Tests.Help;

/// <summary>
/// Every help topic opens a page that exists, and every user-guide page is reachable.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that makes in-application help worth having. Help is a set of paths held
/// in one file and a set of pages held in another, and nothing but this connects them: rename
/// a page and the enum still compiles, the application still builds, and F1 opens a browser at
/// a file that is not there. That failure surfaces to the user and to nobody else.
/// </para>
/// <para>
/// Asserted against the Markdown sources rather than the built HTML, so it holds on a machine
/// with no documentation toolchain — which is most of them.
/// </para>
/// </remarks>
public sealed class HelpPageTests
{
    [SourceTreeFact]
    public void Every_topic_opens_a_page_that_exists()
    {
        List<string> missing = [];

        foreach (HelpTopic topic in HelpTopics.All)
        {
            if (!File.Exists(SourceFor(HelpTopics.PageFor(topic))))
            {
                missing.Add($"{topic} -> {HelpTopics.PageFor(topic)}");
            }
        }

        missing.ShouldBeEmpty(
            "A help topic points at a page that is not in docs/. Either the page was renamed "
            + "and HelpTopics was not, or the topic was added without one.");
    }

    [SourceTreeFact]
    public void Every_user_guide_page_is_reachable_from_a_topic()
    {
        HashSet<string> addressed =
        [
            .. HelpTopics.All
                .Select(HelpTopics.PageFor)
                .Where(page => page.StartsWith("user/", StringComparison.Ordinal))
                .Select(page => Path.GetFileNameWithoutExtension(page)),
        ];

        // The section index is a list of the others; nothing should be opening it directly.
        string[] unreachable =
        [
            .. Directory
                .EnumerateFiles(Path.Combine(SourceTree.Root!, "docs", "user"), "*.md")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name is not "index")
                .Where(name => !addressed.Contains(name!))!,
        ];

        unreachable.ShouldBeEmpty(
            "A page was written into the user guide that no screen can open. Give it a "
            + "HelpTopic, or move it out of docs/user/.");
    }

    /// <summary>The Markdown a built page came from: `user/reports.html` -> `docs/user/reports.md`.</summary>
    private static string SourceFor(string page) => Path.Combine(
        SourceTree.Root!,
        "docs",
        Path.ChangeExtension(page, ".md").Replace('/', Path.DirectorySeparatorChar));
}
