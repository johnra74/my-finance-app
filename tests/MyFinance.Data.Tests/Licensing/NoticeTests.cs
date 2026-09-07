using System.Xml.Linq;
using MyFinance.Data.Tests.Sources;

namespace MyFinance.Data.Tests.Licensing;

/// <summary>
/// The licence and the third-party notices, checked against what the build actually ships.
/// </summary>
/// <remarks>
/// <para>
/// SQLCipher's BSD-3 terms and the Apache-2.0 terms on SQLitePCLRaw and the embedding model
/// all require their copyright notice to be reproduced in a binary distribution. MyFinance
/// publishes as one self-contained file, so the notice has to be inside it — and has to stay
/// correct as dependencies change.
/// </para>
/// <para>
/// That last part is what these tests are for. A licence file rots silently: nothing breaks
/// when a package is added and the notice is not, and nobody notices for a year. Here, adding
/// a dependency fails the build until it is attributed.
/// </para>
/// </remarks>
public sealed class NoticeTests
{
    private static string ReadRoot(string name) =>
        File.ReadAllText(Path.Combine(SourceTree.Root!, name));

    [SourceTreeFact]
    public void The_project_is_licensed_under_apache_two()
    {
        string licence = ReadRoot("LICENSE");

        licence.ShouldContain("Apache License");
        licence.ShouldContain("Version 2.0, January 2004");
        licence.ShouldContain("http://www.apache.org/licenses/LICENSE-2.0");

        // The appendix boilerplate filled in rather than left with its brackets, which is what
        // the appendix itself asks for.
        licence.ShouldContain("Copyright 2026 The MyFinance Authors");
        licence.ShouldNotContain("[name of copyright owner]");
    }

    /// <summary>
    /// Every package the build declares must be named in NOTICE, shipped or not.
    /// </summary>
    /// <remarks>
    /// Total rather than filtered to the ones that reach the executable, because deciding
    /// which those are is exactly the judgement that goes wrong quietly. A build-time-only
    /// package is cheap to list under its own heading; a shipped one that nobody listed is a
    /// licence breach.
    /// </remarks>
    [SourceTreeFact]
    public void Every_declared_package_is_named_in_the_notice()
    {
        string notice = ReadRoot("NOTICE");

        List<string> declared =
        [
            .. XDocument.Load(Path.Combine(SourceTree.Root!, "Directory.Packages.props"))
                .Descendants("PackageVersion")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!),
        ];

        declared.ShouldNotBeEmpty();

        List<string> missing = [.. declared.Where(id => !notice.Contains(id, StringComparison.Ordinal))];

        missing.ShouldBeEmpty(
            "A package is declared in Directory.Packages.props but not named in NOTICE: "
            + string.Join(", ", missing)
            + ". Add it there — several of the licences MyFinance ships under require the "
            + "notice to travel with the binary.");
    }

    /// <summary>
    /// The pieces with no package id of their own, which is why they are the easiest to lose.
    /// </summary>
    /// <remarks>
    /// SQLCipher and LibTomCrypt arrive inside SQLitePCLRaw's native bundle and appear nowhere
    /// in the project files. The model weights are downloaded by a script and are not in the
    /// repository at all. Nothing about a routine dependency review would surface any of them.
    /// </remarks>
    [SourceTreeFact]
    public void The_things_that_ship_without_a_package_id_are_named_too()
    {
        string notice = ReadRoot("NOTICE");

        foreach (string required in (string[])
            ["SQLCipher", "LibTomCrypt", "all-MiniLM-L6-v2", "Sphinx", "Furo"])
        {
            notice.ShouldContain(required);
        }
    }

    /// <summary>
    /// Both files must be embedded, or the copy that travels with the executable is gone.
    /// </summary>
    [SourceTreeFact]
    public void The_licence_and_the_notice_are_embedded_in_the_application()
    {
        string project = ReadRoot(Path.Combine("src", "MyFinance.App", "MyFinance.App.csproj"));

        project.ShouldContain("LogicalName=\"LICENSE\"");
        project.ShouldContain("LogicalName=\"NOTICE\"");
    }

    /// <summary>And must land beside it in the published folder, for anyone not launching it.</summary>
    [SourceTreeFact]
    public void Both_files_are_published_beside_the_executable()
    {
        ReadRoot("build.sh").ShouldContain("cp LICENSE NOTICE");
        ReadRoot("build.ps1").ShouldContain("'LICENSE'");
    }

    /// <summary>
    /// The trademark position, stated once and kept.
    /// </summary>
    /// <remarks>
    /// This project names someone else's product on nearly every page, which is fair use for
    /// interoperability and is worth saying out loud rather than leaving to be inferred.
    /// </remarks>
    [SourceTreeFact]
    public void The_notice_disclaims_any_association_with_microsoft()
    {
        string notice = ReadRoot("NOTICE");

        notice.ShouldContain("Microsoft Money");
        notice.ShouldContain("not affiliated with");
    }
}
