using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Import.Model;
using MyFinance.Import.Mny;
using MyFinance.Import.Mny.Jet;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>One account in the preview or the summary.</summary>
public sealed class MigrationAccountRow
{
    public required MigrationAccountPreview Row { get; init; }

    public string Name => Row.Name;

    public string TypeText => AccountTypeNames.Describe(Row.Type);

    public string StateText => Row.IsClosed ? "Closed" : "Open";

    public int TransactionCount => Row.TransactionCount;

    public Money Balance => Row.Balance;

    public string BalanceText => Balance.ToAccountingString(CultureInfo.CurrentCulture);
}

/// <summary>Which step of the wizard is showing.</summary>
public enum MigrationStep
{
    ChooseFile = 0,
    Review = 1,
    Done = 2,
}

/// <summary>
/// Brings a Microsoft Money book across.
/// </summary>
/// <remarks>
/// The wizard is built around one idea: the user has to be able to check the result. Money
/// is still installed on their machine and still shows its own account list, so the last
/// step puts the migrated balances side by side with what Money reports and says plainly
/// that they should match. A migration nobody can verify is a migration nobody should trust.
/// </remarks>
public sealed partial class MigrationWizardViewModel : DialogViewModel
{
    private readonly MigrationService _migration;
    private readonly IDialogService _dialogs;

    private readonly IHelpService? _help;

    public MigrationWizardViewModel(
        MigrationService migration,
        IDialogService dialogs,
        IHelpService? help = null)
    {
        _migration = migration;
        _dialogs = dialogs;
        _help = help;
    }

    public override string Title => "Bring across a Microsoft Money file";

    /// <summary>
    /// Opens the documentation about migrating.
    /// </summary>
    /// <remarks>
    /// The one modal worth wiring up: it is long, it is the least reversible thing here, and
    /// it asks the user to choose between options whose consequences take a paragraph to
    /// explain — which is a paragraph that belongs in the documentation, not in a tooltip.
    /// </remarks>
    [RelayCommand]
    private void Help() => _help?.Open(HelpTopic.Migrating);

    public ObservableCollection<MigrationAccountRow> Accounts { get; } = [];

    public ObservableCollection<ImportDiagnostic> Diagnostics { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosingFile))]
    [NotifyPropertyChangedFor(nameof(IsReviewing))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    private MigrationStep _step = MigrationStep.ChooseFile;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _periodText = string.Empty;

    [ObservableProperty]
    private int _categoryCount;

    [ObservableProperty]
    private int _payeeCount;

    [ObservableProperty]
    private int _transactionCount;

    [ObservableProperty]
    private int _splitCount;

    [ObservableProperty]
    private int _scheduledCount;

    [ObservableProperty]
    private Money _total;

    [ObservableProperty]
    private bool _includeClosedAccounts = true;

    [ObservableProperty]
    private bool _includeScheduledInstances = true;

    [ObservableProperty]
    private bool _targetBookIsEmpty = true;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public bool IsChoosingFile => Step == MigrationStep.ChooseFile;

    public bool IsReviewing => Step == MigrationStep.Review;

    public bool IsDone => Step == MigrationStep.Done;

    public string TotalText => Total.ToAccountingString(CultureInfo.CurrentCulture);

    public bool HasScheduled => ScheduledCount > 0;

    /// <summary>Why the projected bills are kept, in the words the checkbox needs.</summary>
    public string ScheduledExplanation =>
        $"{ScheduledCount:N0} of these are bills Money wrote into the register but nobody ever entered. "
        + "Money counts them in the balances it shows, so keeping them is what makes the figures below match. "
        + "Leaving them out gives a tidier book that will not reconcile against Money.";

    public string BlockedExplanation =>
        "This book already has accounts or transactions in it. A Money file can only be brought into an empty book — "
        + "close this, create a new book from the File menu, and migrate into that.";

    private MigrationPreview? _preview;

    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        string? path = _dialogs.PickMoneyFile();

        if (path is null)
        {
            return;
        }

        await LoadAsync(path).ConfigureAwait(true);
    }

    /// <summary>Reads the file and shows what is in it, writing nothing.</summary>
    public async Task LoadAsync(string path)
    {
        ErrorMessage = null;

        try
        {
            MigrationOptions options = CurrentOptions();

            _preview = await RunBusyAsync(
                "Opening the Money file",
                (report, token) => _migration.PreviewAsync(path, options, report, token))
                .ConfigureAwait(true);

            if (_preview is null)
            {
                return;
            }

            Apply(_preview);
            Step = MigrationStep.Review;
        }
        catch (JetException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (IOException ex)
        {
            ErrorMessage = $"That file could not be read: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task MigrateAsync()
    {
        if (_preview is null || !TargetBookIsEmpty)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Bring across",
            $"Bring {TransactionCount:N0} transactions across from {FileName}?\n\n"
            + "Your Money file is only read, never changed."))
        {
            return;
        }

        ErrorMessage = null;

        try
        {
            MoneyBook book = _preview.Book;
            MigrationOptions options = CurrentOptions();

            MigrationSummary? summary = await RunBusyAsync(
                "Bringing the book across",
                (report, token) => _migration.MigrateAsync(book, options, report, token))
                .ConfigureAwait(true);

            if (summary is null)
            {
                // Stopped part-way. The write ran inside a transaction, so the book is
                // untouched and the user is back where they started rather than half migrated.
                ErrorMessage = WasCancelled
                    ? "Stopped. Nothing was written — the book is exactly as it was."
                    : null;
                return;
            }

            Show(summary);
            Step = MigrationStep.Done;
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Finish() => Close(true);

    [RelayCommand]
    private void Back()
    {
        Step = MigrationStep.ChooseFile;
        _preview = null;
        Accounts.Clear();
        Diagnostics.Clear();
    }

    private MigrationOptions CurrentOptions() => new()
    {
        IncludeClosedAccounts = IncludeClosedAccounts,
        IncludeScheduledInstances = IncludeScheduledInstances,
    };

    private void Apply(MigrationPreview preview)
    {
        FileName = preview.FileName;
        CategoryCount = preview.CategoryCount;
        PayeeCount = preview.PayeeCount;
        TransactionCount = preview.TransactionCount;
        SplitCount = preview.SplitCount;
        ScheduledCount = preview.ScheduledCount;
        Total = preview.Total;
        TargetBookIsEmpty = preview.TargetBookIsEmpty;

        PeriodText = preview.EarliestDate is DateOnly from && preview.LatestDate is DateOnly to
            ? $"{from:d MMMM yyyy} through {to:d MMMM yyyy}"
            : "No dated transactions";

        Fill(preview.Accounts, preview.Diagnostics);

        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(HasScheduled));
        OnPropertyChanged(nameof(ScheduledExplanation));
    }

    private void Show(MigrationSummary summary)
    {
        Total = summary.Total;
        Fill(summary.Accounts, summary.Diagnostics);

        SummaryText =
            $"{summary.AccountsCreated:N0} accounts, {summary.CategoriesCreated:N0} categories, "
            + $"{summary.PayeesCreated:N0} payees and {summary.TransactionsCreated:N0} transactions came across, "
            + $"including {summary.SplitsCreated:N0} split lines and {summary.TransfersLinked:N0} transfers.";

        OnPropertyChanged(nameof(TotalText));
    }

    private void Fill(
        IReadOnlyList<MigrationAccountPreview> accounts,
        IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        Accounts.Clear();
        Diagnostics.Clear();

        foreach (MigrationAccountPreview account in accounts)
        {
            Accounts.Add(new MigrationAccountRow { Row = account });
        }

        foreach (ImportDiagnostic diagnostic in diagnostics)
        {
            Diagnostics.Add(diagnostic);
        }
    }

    // Re-reading is cheap next to getting the answer wrong, and the options change the
    // figures the user is about to check against Money.
    partial void OnIncludeClosedAccountsChanged(bool value) => Reload();

    partial void OnIncludeScheduledInstancesChanged(bool value) => Reload();

    private void Reload()
    {
        if (_preview is not null && Step == MigrationStep.Review)
        {
            Apply(_preview with
            {
                Accounts = Recompute(),
            });
        }
    }

    /// <summary>Recomputes the preview rows for the options as they now stand.</summary>
    private IReadOnlyList<MigrationAccountPreview> Recompute()
    {
        var rows = new List<MigrationAccountPreview>();

        if (_preview is null)
        {
            return rows;
        }

        foreach (var account in _preview.Book.Accounts)
        {
            if (!IncludeClosedAccounts && account.IsClosed)
            {
                continue;
            }

            List<Money> amounts =
            [
                .. _preview.Book.TopLevelTransactions
                    .Where(t => t.AccountId == account.Id
                        && (IncludeScheduledInstances || !t.IsScheduledInstance))
                    .Select(t => t.Amount),
            ];

            rows.Add(new MigrationAccountPreview(
                account.Id,
                account.Name,
                account.Type,
                account.IsClosed,
                amounts.Count,
                account.OpeningBalance + Money.Sum(amounts)));
        }

        return rows;
    }
}
