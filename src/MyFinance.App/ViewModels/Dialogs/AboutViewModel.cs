using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>
/// What this build is, and what it is made of.
/// </summary>
/// <remarks>
/// <para>
/// This exists for two reasons, and the second is the load-bearing one. The first is the
/// ordinary About box question — which version am I running. The second is that MyFinance
/// ships as one self-contained file carrying SQLCipher, the .NET runtime, an embedding model
/// and the help pages, and several of those licences require their copyright notice to be
/// reproduced wherever the binary goes. A text file beside the executable does not satisfy
/// that for long: it is the file a user moves, or zips without, or never sees. Reading it out
/// of the executable is the only copy that cannot be separated from it.
/// </para>
/// <para>
/// Reachable from the unlock screen as well as the shell, so the notices can be read without
/// a password — somebody auditing the binary is not necessarily the person who owns the book.
/// </para>
/// </remarks>
public sealed partial class AboutViewModel : DialogViewModel
{
    private readonly IHelpService _help;

    public AboutViewModel(IHelpService help)
    {
        _help = help;
    }

    public override string Title => "About MyFinance";

    /// <summary>
    /// Everything shown here is an instance property, including the ones with no state.
    /// </summary>
    /// <remarks>
    /// A WPF binding resolves against the data context, so a <c>static</c> property binds to
    /// nothing and fails silently — an About box that shows a blank line where the copyright
    /// should be, which is exactly the line that has to be there.
    /// </remarks>
    public string ProductVersion =>
        typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public string VersionText => $"Version {ProductVersion}";

    public string CopyrightText =>
        typeof(AboutViewModel).Assembly
            .GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? "Copyright © The MyFinance Authors";

    public string SummaryText =>
        "Personal finance for Windows: an encrypted book, a register, bills, budgets and "
        + "reports. Everything runs on this machine. Nothing is sent anywhere.";

    public string LicenceSummary =>
        "MyFinance is free software under the Apache License, Version 2.0.";

    /// <summary>The licence, read out of this executable.</summary>
    public string LicenceText => Read("LICENSE");

    /// <summary>Third-party notices, read out of this executable.</summary>
    public string NoticeText => Read("NOTICE");

    [RelayCommand]
    private void OpenHelp() => _help.Open(HelpTopic.Contents);

    [RelayCommand]
    private void Done() => Close(true);

    /// <summary>
    /// Reads an embedded text resource, or says plainly that it is missing.
    /// </summary>
    /// <remarks>
    /// Never throws. An About box that crashes while displaying a licence would be a poor
    /// joke, and the notices are wanted most in exactly the situation where something else has
    /// already gone wrong.
    /// </remarks>
    private static string Read(string name)
    {
        try
        {
            using Stream? stream = typeof(AboutViewModel).Assembly.GetManifestResourceStream(name);

            if (stream is null)
            {
                return $"The {name} file was not embedded in this build. "
                    + "It is at the root of the source repository.";
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            return $"The {name} file could not be read: {ex.Message}";
        }
    }
}
