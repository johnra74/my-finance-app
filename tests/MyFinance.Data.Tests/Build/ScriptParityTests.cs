using System.Text.RegularExpressions;
using MyFinance.Data.Tests.Sources;

namespace MyFinance.Data.Tests.Build;

/// <summary>
/// The build scripts, checked for parity between the two platforms.
/// </summary>
/// <remarks>
/// <para>
/// Development happens on Linux and the application only runs on Windows, so both sets of
/// scripts have to work and only one of them gets exercised day to day. That asymmetry is how
/// <c>tools/fetch-model.sh</c> went for months with no Windows counterpart: a Windows-only
/// developer could not fetch the embedding model at all, and nothing said so.
/// </para>
/// <para>
/// These tests are structural on purpose. They cannot run a PowerShell script from a Linux
/// test host, so they check the thing that actually rots — that the counterpart exists, and
/// that the two entry points offer the same commands.
/// </para>
/// </remarks>
public sealed class ScriptParityTests
{
    /// <summary>
    /// Scripts belonging to the project.
    /// </summary>
    /// <remarks>
    /// Hidden files and directories are skipped: <c>.claude/</c> holds editor and agent
    /// tooling that is not the project's build, and requiring a Windows twin for it would be
    /// noise rather than a guarantee.
    /// </remarks>
    private static IEnumerable<string> Scripts(string extension) =>
        Directory.EnumerateFiles(SourceTree.Root!, "*" + extension, SearchOption.AllDirectories)
            .Where(path => !Path
                .GetRelativePath(SourceTree.Root!, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment =>
                    segment.StartsWith('.')
                    || segment is "bin" or "obj" or "artifacts" or "node_modules"));

    private static string Relative(string path) => Path.GetRelativePath(SourceTree.Root!, path);

    [SourceTreeFact]
    public void Every_shell_script_has_a_powershell_counterpart()
    {
        List<string> scripts = [.. Scripts(".sh")];

        scripts.ShouldNotBeEmpty("No .sh scripts were found at all, which means this test is "
            + "looking in the wrong place rather than that the repository has none.");

        List<string> missing =
        [
            .. scripts
                .Where(sh => !File.Exists(Path.ChangeExtension(sh, ".ps1")))
                .Select(Relative),
        ];

        missing.ShouldBeEmpty(
            "A shell script has no Windows counterpart: " + string.Join(", ", missing)
            + ". Windows is the only platform this application runs on, so anything a "
            + "developer needs must be runnable there.");
    }

    /// <summary>
    /// Each PowerShell script needs a <c>.cmd</c> shim beside it.
    /// </summary>
    /// <remarks>
    /// Windows 11 ships with script execution disabled, so a bare <c>.ps1</c> fails with
    /// "running scripts is disabled on this system" on a machine straight out of the box.
    /// The shim bypasses the policy for that one script and changes nothing machine-wide.
    /// This is `011-packaging-and-release` FR-012.
    /// </remarks>
    [SourceTreeFact]
    public void Every_powershell_script_has_a_shim_that_survives_the_execution_policy()
    {
        List<string> missing =
        [
            .. Scripts(".ps1")
                .Where(ps1 => !File.Exists(Path.ChangeExtension(ps1, ".cmd")))
                .Select(Relative),
        ];

        missing.ShouldBeEmpty(
            "A PowerShell script has no .cmd shim: " + string.Join(", ", missing)
            + ". Without one it cannot be run on a stock Windows 11 machine.");
    }

    /// <summary>
    /// Every task the Linux build offers must be offered by the Windows build too.
    /// </summary>
    /// <remarks>
    /// One-directional deliberately. `build.ps1` carries `run` and `ef`, which the Linux
    /// script cannot meaningfully offer — WPF does not run there. A task existing only on the
    /// Linux side is the gap worth failing over.
    /// </remarks>
    [SourceTreeFact]
    public void Every_task_the_linux_build_offers_is_offered_on_windows_too()
    {
        string shell = File.ReadAllText(Path.Combine(SourceTree.Root!, "build.sh"));
        string windows = File.ReadAllText(Path.Combine(SourceTree.Root!, "build.ps1"));

        // The labels of the dispatch `case`, e.g. "  publish) publish ..." — but not its
        // catch-all `*)`.
        string block = shell[shell.IndexOf("\ncase ", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("\nesac", StringComparison.Ordinal)];

        List<string> tasks =
        [
            .. Regex.Matches(block, @"^\s{2}([a-z]+)\)", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value),
        ];

        tasks.ShouldContain("build");
        tasks.ShouldContain("publish");

        // The ValidateSet that lists the tasks, as opposed to the one listing configurations.
        Match set = Regex.Matches(windows, @"\[ValidateSet\(([^)]*)\)\]")
            .First(m => m.Value.Contains("'build'", StringComparison.Ordinal));

        List<string> offered =
        [
            .. Regex.Matches(set.Groups[1].Value, @"'([^']+)'").Select(m => m.Groups[1].Value),
        ];

        List<string> missing = [.. tasks.Where(t => !offered.Contains(t))];

        missing.ShouldBeEmpty(
            "build.sh offers a task that build.ps1 does not: " + string.Join(", ", missing));
    }
}
