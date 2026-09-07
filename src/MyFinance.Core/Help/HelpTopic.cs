namespace MyFinance.Core.Help;

/// <summary>
/// A page of the documentation a screen can open.
/// </summary>
/// <remarks>
/// An enum rather than a string, for the same reason the diagnostics log takes one: a path
/// typed at each call site is a path that goes stale silently, and a Help button that opens
/// nothing is worse than no Help button. A test asserts every value here resolves to a page
/// that exists in <c>docs/</c>, so renaming a page fails the build.
/// </remarks>
public enum HelpTopic
{
    /// <summary>The front page. Where a screen with nothing more specific to say points.</summary>
    Contents = 0,

    Installing,
    FirstBook,
    AccountsAndRegister,
    Importing,
    Categorizing,
    Migrating,
    Bills,
    Budgets,
    Reports,
    Investments,
    Printing,
    Exporting,
    Backups,
    Security,
    Troubleshooting,
    Keyboard,
}

/// <summary>
/// Where each topic lives in the built documentation.
/// </summary>
/// <remarks>
/// <para>
/// In <c>MyFinance.Core</c> rather than the WPF layer so it can be tested without Windows —
/// principle 4. The paths are relative and are combined with the folder the documentation was
/// extracted into, which is why <see cref="PageFor"/> is checked for the things that would
/// make that combination escape it.
/// </para>
/// <para>
/// Only the user guide is addressed. The developer pages ship too and are reachable by
/// navigating; no screen in the application should be offering them.
/// </para>
/// </remarks>
public static class HelpTopics
{
    /// <summary>The page every unmapped screen falls back to.</summary>
    public const string ContentsPage = "index.html";

    /// <summary>The relative path of a topic's page, using forward slashes.</summary>
    public static string PageFor(HelpTopic topic) => topic switch
    {
        HelpTopic.Contents => ContentsPage,
        HelpTopic.Installing => "user/installing.html",
        HelpTopic.FirstBook => "user/first-book.html",
        HelpTopic.AccountsAndRegister => "user/accounts-and-register.html",
        HelpTopic.Importing => "user/importing.html",
        HelpTopic.Categorizing => "user/categorizing.html",
        HelpTopic.Migrating => "user/migrating.html",
        HelpTopic.Bills => "user/bills.html",
        HelpTopic.Budgets => "user/budgets.html",
        HelpTopic.Reports => "user/reports.html",
        HelpTopic.Investments => "user/investments.html",
        HelpTopic.Printing => "user/printing.html",
        HelpTopic.Exporting => "user/exporting.html",
        HelpTopic.Backups => "user/backups.html",
        HelpTopic.Security => "user/security.html",
        HelpTopic.Troubleshooting => "user/troubleshooting.html",
        HelpTopic.Keyboard => "reference/keyboard.html",

        // A topic added to the enum and not to this map would otherwise open the front page
        // and look like it had worked.
        _ => throw new ArgumentOutOfRangeException(
            nameof(topic), topic, "That help topic has no page."),
    };

    /// <summary>Every topic there is.</summary>
    public static IReadOnlyList<HelpTopic> All { get; } = [.. Enum.GetValues<HelpTopic>()];
}
