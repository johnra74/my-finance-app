using System.Text.RegularExpressions;
using MyFinance.Data.Tests.Sources;

namespace MyFinance.Data.Tests.Build;

/// <summary>
/// Arguments the PowerShell scripts hand to external tools.
/// </summary>
/// <remarks>
/// <para>
/// PowerShell reads an unquoted <c>-name:value</c> token as its own parameter syntax, not as
/// a string to pass along. So <c>dotnet publish -p:PublishSingleFile=true</c> written the
/// obvious way never reaches dotnet: PowerShell binds <c>-p</c> to the common parameter
/// <c>-PipelineVariable</c> and, given four such properties, refuses before dotnet is invoked
/// with <i>"Cannot bind parameter because parameter 'p' is specified more than once"</i>.
/// </para>
/// <para>
/// That shipped, and `build publish` could never have worked on Windows. It is invisible from
/// Linux, where the equivalent line in <c>build.sh</c> is ordinary shell. Quoting fixes it;
/// this test is what stops the quotes being dropped again by someone tidying up.
/// </para>
/// <para>
/// The rule is applied to every such token rather than only the ones that reach a PowerShell
/// function. Calling an external executable with <c>&amp;</c> does no parameter binding, so
/// <c>&amp; $sphinx -D:key=value</c> would in fact be safe — but quoting it is safe too, and
/// a rule that needs the reader to work out which side of that line a call falls on is a rule
/// that will be got wrong.
/// </para>
/// </remarks>
public sealed class PowerShellArgumentTests
{
    /// <summary>
    /// A single-dash token with a colon in it: <c>-p:X=Y</c>, <c>-property:X=Y</c>.
    /// </summary>
    /// <remarks>
    /// A leading <c>-</c> not preceded by a quote or another dash. Double-dash arguments such
    /// as <c>--configuration</c> are never read as parameter names and are left alone.
    /// </remarks>
    private static readonly Regex Unquoted = new(@"(?<![-'""\w])-[A-Za-z][\w-]*:", RegexOptions.Compiled);

    private static readonly Regex BlockComment = new(@"<#.*?#>", RegexOptions.Singleline | RegexOptions.Compiled);

    [SourceTreeFact]
    public void Msbuild_properties_in_powershell_scripts_are_quoted()
    {
        List<string> offenders = [];

        foreach (string script in Directory.EnumerateFiles(
            SourceTree.Root!, "*.ps1", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(SourceTree.Root!, script);

            if (relative.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment.StartsWith('.') || segment is "bin" or "obj" or "artifacts"))
            {
                continue;
            }

            // Comments discuss this very syntax, so they are removed before looking for it.
            string text = BlockComment.Replace(File.ReadAllText(script), string.Empty);

            int number = 0;

            foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
            {
                number++;

                if (line.TrimStart().StartsWith('#') || !Unquoted.IsMatch(line))
                {
                    continue;
                }

                offenders.Add($"{relative}:{number}  {line.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "An MSBuild-style argument is unquoted in a PowerShell script, so PowerShell will "
            + "read it as a parameter of its own rather than passing it on:\n"
            + string.Join("\n", offenders)
            + "\nWrap it in single quotes.");
    }
}
