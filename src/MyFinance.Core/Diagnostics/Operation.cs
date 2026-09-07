namespace MyFinance.Core.Diagnostics;

/// <summary>
/// What the application was doing. The entire caller-facing vocabulary of the log.
/// </summary>
/// <remarks>
/// <para>
/// An enum rather than a string, and that is the whole point. A log of a personal finance
/// application is one careless <c>$"Failed for {payee}"</c> away from being a second copy of
/// the user's records, in plain text, in a file they may well email to somebody. There are 59
/// <c>catch</c> blocks in this solution; a rule that each of them must remember is a rule that
/// will be broken.
/// </para>
/// <para>
/// So the writer offers no parameter that could carry a payee, an amount or a path. Redaction
/// stops being discipline and becomes something the compiler enforces.
/// </para>
/// </remarks>
public enum Operation
{
    Unknown = 0,

    // -- The book itself ----------------------------------------------------------------
    OpenBook,
    CreateBook,
    ChangePassword,
    UpgradeSchema,
    CloseBook,

    // -- The register -------------------------------------------------------------------
    LoadRegister,
    SaveTransaction,
    DeleteTransaction,
    Reconcile,

    // -- Reference data -----------------------------------------------------------------
    LoadAccounts,
    SaveAccount,
    LoadCategories,
    SaveCategory,
    LoadPayees,
    SavePayee,

    // -- Getting data in and out --------------------------------------------------------
    ReadStatementFile,
    PrepareImport,
    CommitImport,
    UndoImport,
    ReadMoneyFile,
    Migrate,
    ExportBook,
    ExportReport,
    Print,

    // -- Everything else the user can start ---------------------------------------------
    RunReport,
    LoadBudget,
    SaveBudget,
    LoadBills,
    EnterScheduled,
    AutoEnterScheduled,
    Forecast,
    SuggestCategory,
    BuildSuggestionIndex,
    BackUp,
    Restore,
    OpenHelp,

    // -- Where an unhandled failure surfaced --------------------------------------------
    UnhandledOnInterfaceThread,
    UnhandledOnBackgroundThread,
    UnobservedTask,
    Startup,
    Navigate,
}
