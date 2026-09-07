using System.IO;
using System.IO.Compression;
using System.Reflection;
using MyFinance.Core.Diagnostics;
using MyFinance.Core.Help;

namespace MyFinance.App.Services;

/// <summary>Opens a page of the documentation.</summary>
public interface IHelpService
{
    /// <summary>Shows the page for a topic. Never throws.</summary>
    void Open(HelpTopic topic);
}

/// <summary>
/// The documentation, shipped inside the executable and read in the user's browser.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bundled rather than linked.</b> Pointing at the hosted copy would tell that site this
/// machine's address and that it runs MyFinance, at the moment somebody is asking a question
/// about their own finances — principle 7. It would also fail without a connection and, during
/// an upgrade, describe a version that is not the one running. The pages ship with the build
/// they document and cost about 300 KB in an 85 MB file.
/// </para>
/// <para>
/// <b>The browser rather than a viewer.</b> WebView2 is exactly the runtime dependency the
/// single-file build exists to avoid, and rendering the Markdown a second way would produce
/// pages that disagree with the real ones. A browser already has search, back, zoom and print.
/// </para>
/// <para>
/// Embedded conditionally, so a build made without the documentation toolchain still works and
/// simply says the pages are not there — the same bargain the embedding model makes.
/// </para>
/// </remarks>
public sealed class HelpService : IHelpService
{
    /// <summary>Matches the LogicalName in MyFinance.App.csproj.</summary>
    private const string ResourceName = "help.zip";

    public const string HelpFolderName = "help";

    private readonly IDialogService _dialogs;
    private readonly DiagnosticSink _diagnostics;

    public HelpService(IDialogService dialogs, DiagnosticSink diagnostics)
    {
        _dialogs = dialogs;
        _diagnostics = diagnostics;
    }

    /// <summary>Where this build's pages are unpacked to.</summary>
    /// <remarks>
    /// Under the version, so upgrading does not leave the previous build's pages in place to be
    /// read as though they described the current one. Local application data rather than beside
    /// the executable, which may well be on a memory stick or a read-only share.
    /// </remarks>
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create),
        DiagnosticLogFactory.FolderName,
        HelpFolderName,
        Version);

    private static string Version =>
        typeof(HelpService).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Whether this build shipped with the documentation in it.</summary>
    public static bool IsAvailable =>
        typeof(HelpService).Assembly.GetManifestResourceInfo(ResourceName) is not null;

    public void Open(HelpTopic topic)
    {
        if (!IsAvailable)
        {
            // Not an error: a solution built without Python has no pages to give. Saying where
            // they are is more use than an apology.
            _dialogs.ShowInformation(
                "Help",
                "This build of MyFinance was made without its documentation.\n\n"
                + "The pages are in the docs folder of the source repository, and are readable "
                + "as they are.");
            return;
        }

        try
        {
            string page = Path.Combine(EnsureExtracted(), HelpTopics.PageFor(topic).Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(page))
            {
                // A topic whose page did not survive a documentation change. The front page is
                // a worse answer than the right one and a much better answer than nothing.
                page = Path.Combine(EnsureExtracted(), HelpTopics.ContentsPage);
            }

            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true };
            process.Start();
        }
        catch (Exception ex)
        {
            // Help must never be the thing that takes the application down. Recorded so a
            // failure here is investigable, and reported as a sentence rather than a stack.
            _diagnostics.Failure(Operation.OpenHelp, ex);

            _dialogs.ShowInformation(
                "Help",
                "The documentation could not be opened.\n\n"
                + $"It should be in:\n{Directory}");
        }
    }

    /// <summary>
    /// Unpacks the pages if they are not already there, and returns where they are.
    /// </summary>
    /// <remarks>
    /// Extracted beside its destination and then moved into place, so an extraction interrupted
    /// half way leaves nothing that the next launch would find and trust.
    /// </remarks>
    private static string EnsureExtracted()
    {
        string target = Directory;

        if (File.Exists(Path.Combine(target, HelpTopics.ContentsPage)))
        {
            return target;
        }

        string staging = target + ".unpacking";
        Delete(staging);
        Delete(target);

        using (Stream? source = typeof(HelpService).Assembly.GetManifestResourceStream(ResourceName))
        {
            if (source is null)
            {
                throw new InvalidOperationException("The documentation resource could not be read.");
            }

            using var archive = new ZipArchive(source, ZipArchiveMode.Read);
            archive.ExtractToDirectory(staging);
        }

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        System.IO.Directory.Move(staging, target);

        RemoveOtherVersions(target);

        return target;
    }

    /// <summary>Clears out the pages an earlier build unpacked.</summary>
    /// <remarks>
    /// Best effort. A folder that cannot be removed wastes a megabyte and breaks nothing, which
    /// does not justify failing the thing the user actually asked for.
    /// </remarks>
    private static void RemoveOtherVersions(string keep)
    {
        try
        {
            string parent = Path.GetDirectoryName(keep)!;

            foreach (string directory in System.IO.Directory.EnumerateDirectories(parent))
            {
                if (!string.Equals(directory, keep, StringComparison.OrdinalIgnoreCase))
                {
                    Delete(directory);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Delete(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
