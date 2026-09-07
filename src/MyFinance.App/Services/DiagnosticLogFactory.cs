using System.IO;
using MyFinance.Core.Diagnostics;

namespace MyFinance.App.Services;

/// <summary>
/// Decides where the log lives.
/// </summary>
/// <remarks>
/// <para>
/// The user's local application data, deliberately <b>not</b> beside the book. A book
/// frequently sits in OneDrive or Dropbox, and a log written next to it would be copied off
/// the machine as a side effect of where the book happens to live — which is not something
/// anybody would choose for a diagnostics file, and is against the spirit of the rule that
/// nothing here leaves the machine.
/// </para>
/// <para>
/// "Local" rather than "roaming" for the same reason: roaming application data syncs between
/// machines on a domain.
/// </para>
/// <para>
/// Being harder to find is the cost, and it is paid by the Diagnostics command in the top bar
/// opening the folder in one click.
/// </para>
/// </remarks>
public static class DiagnosticLogFactory
{
    public const string FolderName = "MyFinance";

    public const string LogsFolderName = "logs";

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create),
        FolderName,
        LogsFolderName);

    public static DiagnosticLog Create(bool verbose = false) =>
        new(new DiagnosticOptions(Directory, Verbose: verbose));
}
