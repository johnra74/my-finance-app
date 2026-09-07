using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Printing;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>One row of the register grid.</summary>
public sealed class RegisterRowViewModel
{
    public required RegisterLine Line { get; init; }

    public Transaction Transaction => Line.Transaction;

    public int Id => Transaction.Id;

    public DateOnly Date => Transaction.Date;

    public string? Number => Transaction.Number;

    public string PayeeName => Transaction.Payee?.Name ?? string.Empty;

    /// <summary>What the category column shows: a category, "Split", or the transfer target.</summary>
    public string CategoryText
    {
        get
        {
            if (Transaction.TransferPeer?.Account is Account other)
            {
                return $"Transfer: {other.Name}";
            }

            if (Transaction.IsTransfer)
            {
                return "Transfer";
            }

            if (Transaction.Splits.Count > 1)
            {
                return "— Split —";
            }

            return Transaction.Splits.FirstOrDefault()?.Category?.FullName ?? string.Empty;
        }
    }

    public string? Memo => Transaction.Memo;

    /// <summary>Money out, shown as a positive figure in its own column.</summary>
    public Money? Payment => Line.Payment;

    /// <summary>Money in, shown as a positive figure in its own column.</summary>
    public Money? Deposit => Line.Deposit;

    public Money Balance => Line.Balance;

    public bool IsVoid => Transaction.IsVoid;

    public bool IsUncategorized => Transaction.IsUncategorized && !Transaction.IsTransfer;

    public ClearedStatus ClearedStatus => Transaction.ClearedStatus;

    /// <summary>The single-letter marker Money puts in the "C" column.</summary>
    public string ClearedMark => Transaction.ClearedStatus switch
    {
        ClearedStatus.Reconciled => "R",
        ClearedStatus.Cleared => "C",
        _ => string.Empty,
    };

    public string PaymentText => Payment?.ToString("N", CultureInfo.CurrentCulture) ?? string.Empty;

    public string DepositText => Deposit?.ToString("N", CultureInfo.CurrentCulture) ?? string.Empty;

    public string BalanceText => Balance.ToAccountingString(CultureInfo.CurrentCulture);

    /// <summary>
    /// The date as the register shows it.
    /// </summary>
    /// <remarks>
    /// Here rather than as a <c>StringFormat</c> in the grid so that the printed page and the
    /// screen cannot drift apart. A printout that disagrees with the screen is the copy
    /// somebody takes to their accountant — see `specs/016-printing`, NFR-002.
    /// </remarks>
    public string DateText => Date.ToString("d", CultureInfo.CurrentCulture);
}

/// <summary>A named date range for the register's view selector.</summary>
/// <param name="Text">How it reads in the picker.</param>
/// <param name="Months">How far back to look, or null for everything.</param>
public readonly record struct RegisterRangeOption(string Text, int? Months);

/// <summary>
/// One account's register: the screen the user spends most of their time in.
/// </summary>
public sealed partial class RegisterPageViewModel : PageViewModel
{
    private readonly RegisterService _register;
    private readonly AccountService _accounts;
    private readonly CategoryService _categories;
    private readonly PayeeService _payees;
    private readonly SuggestionService _suggestions;
    private readonly ReconcileService _reconcile;
    private readonly IModalService _modals;
    private readonly IDialogService _dialogs;
    private readonly IServiceProviderAccessor _services;

    private int _accountId;
    private bool _suspendRefresh;
    private IReadOnlyList<Account> _transferTargets = [];
    private IReadOnlyList<CategoryListItem> _categoryList = [];
    private IReadOnlyList<string> _payeeNames = [];


    public RegisterPageViewModel(
        RegisterService register,
        AccountService accounts,
        CategoryService categories,
        PayeeService payees,
        SuggestionService suggestions,
        ReconcileService reconcile,
        IModalService modals,
        IDialogService dialogs,
        IServiceProviderAccessor services)
    {
        _register = register;
        _accounts = accounts;
        _categories = categories;
        _payees = payees;
        _suggestions = suggestions;
        _reconcile = reconcile;
        _modals = modals;
        _dialogs = dialogs;
        _services = services;
    }

    public override string Title => AccountName;

    public override AppSection Section => AppSection.Banking;

    public override HelpTopic HelpTopic => HelpTopic.AccountsAndRegister;

    public ObservableCollection<RegisterRowViewModel> Rows { get; } = [];

    public IReadOnlyList<RegisterRangeOption> Ranges { get; } =
    [
        new RegisterRangeOption("All transactions", null),
        new RegisterRangeOption("Last 3 months", 3),
        new RegisterRangeOption("Last 12 months", 12),
        new RegisterRangeOption("Last 24 months", 24),
    ];

    [ObservableProperty]
    private string _accountName = "Register";

    [ObservableProperty]
    private RegisterRowViewModel? _selectedRow;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private RegisterRangeOption _selectedRange;

    [ObservableProperty]
    private bool _uncategorizedOnly;

    [ObservableProperty]
    private Money _currentBalance;

    [ObservableProperty]
    private Money _clearedBalance;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _countText = string.Empty;

    public string CurrentBalanceText => CurrentBalance.ToAccountingString(CultureInfo.CurrentCulture);

    public string ClearedBalanceText => ClearedBalance.ToAccountingString(CultureInfo.CurrentCulture);

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Register",
            Links =
            [
                new TaskLink { Text = "New transaction", Execute = () => NewTransactionCommand.Execute(null) },
                new TaskLink { Text = "Import into this account…", Execute = () => ImportStatementCommand.Execute(null) },
                new TaskLink { Text = "Balance this account", Execute = () => ReconcileCommand.Execute(null) },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    /// <summary>Points the page at an account. Called before navigating to it.</summary>
    /// <remarks>
    /// Resetting the filters would otherwise fire a reload per property. They are suspended
    /// so the single load that navigation triggers is the one that decides what is on screen,
    /// rather than three overlapping loads racing to write the same collection.
    /// </remarks>
    public void SetAccount(int accountId)
    {
        _suspendRefresh = true;

        try
        {
            _accountId = accountId;
            SelectedRange = Ranges[0];
            SearchText = null;
            UncategorizedOnly = false;
        }
        finally
        {
            _suspendRefresh = false;
        }
    }

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_accountId == 0 || _suspendRefresh)
        {
            return;
        }

        int accountId = _accountId;
        RegisterFilter filter = BuildFilter();

        // Reloaded on every visit rather than cached: categories and payees can be edited on
        // other screens, and a stale picker is worse than a slow one. All four queries go to
        // the background together; the rows are rebuilt back on the UI thread afterwards.
        var loaded = await RunBusyAsync(
            "Loading the register",
            async (_, token) => (
                Targets: await _accounts.GetAllAsync(cancellationToken: token).ConfigureAwait(false),
                Categories: await _categories.GetAllAsync(cancellationToken: token).ConfigureAwait(false),
                Payees: await _payees.GetNamesAsync(token).ConfigureAwait(false),
                View: await _register.GetRegisterAsync(accountId, filter, token).ConfigureAwait(false)))
            .ConfigureAwait(true);

        if (loaded.View is null)
        {
            return;
        }

        _transferTargets = loaded.Targets;
        _categoryList = loaded.Categories;
        _payeeNames = loaded.Payees;

        RegisterView view = loaded.View;

        AccountName = view.Account.Name;

        int? previous = SelectedRow?.Id;

        Rows.Clear();
        foreach (RegisterLine line in view.Lines)
        {
            Rows.Add(new RegisterRowViewModel { Line = line });
        }

        CurrentBalance = view.CurrentBalance;
        ClearedBalance = view.ClearedBalance;
        IsEmpty = view.TotalCount == 0;

        CountText = view.IsFiltered
            ? $"Showing {view.VisibleCount} of {view.TotalCount}"
            : $"{view.TotalCount} transaction{(view.TotalCount == 1 ? string.Empty : "s")}";

        SelectedRow = Rows.FirstOrDefault(r => r.Id == previous) ?? Rows.LastOrDefault();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CurrentBalanceText));
        OnPropertyChanged(nameof(ClearedBalanceText));
    }

    [RelayCommand]
    private async Task NewTransactionAsync()
    {
        if (_accountId == 0)
        {
            return;
        }

        TransactionEditorViewModel editor = TransactionEditorViewModel.ForNew(
            _register, _payees, _suggestions, _modals, _accountId, _transferTargets, _categoryList, _payeeNames);

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
            SelectedRow = Rows.FirstOrDefault(r => r.Id == editor.SavedId) ?? SelectedRow;
        }
    }

    [RelayCommand]
    private async Task EditTransactionAsync(RegisterRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        Transaction? existing = await _register.FindAsync(row.Id).ConfigureAwait(true);
        if (existing is null)
        {
            await RefreshAsync().ConfigureAwait(true);
            return;
        }

        TransactionEditorViewModel editor = TransactionEditorViewModel.ForExisting(
            _register, _payees, _suggestions, _modals, _accountId, _transferTargets, _categoryList, _payeeNames, existing);

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Duplicates a row into a new entry, which is how most recurring-but-unscheduled
    /// transactions actually get entered.
    /// </summary>
    [RelayCommand]
    private async Task DuplicateTransactionAsync(RegisterRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        Transaction? existing = await _register.FindAsync(row.Id).ConfigureAwait(true);
        if (existing is null)
        {
            return;
        }

        var draft = new TransactionDraft
        {
            AccountId = _accountId,
            Date = DateOnly.FromDateTime(DateTime.Today),
            Number = existing.Number,
            PayeeName = existing.Payee?.Name,
            Memo = existing.Memo,
            Amount = existing.Amount,
            ClearedStatus = ClearedStatus.Uncleared,
            TransferAccountId = existing.TransferPeer?.AccountId,
            Splits =
            [
                .. existing.Splits.Select(s => new SplitDraft
                {
                    CategoryId = s.CategoryId,
                    Amount = s.Amount,
                    Memo = s.Memo,
                })
            ],
        };

        try
        {
            await _register.SaveAsync(draft).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Duplicate transaction", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteTransactionAsync(RegisterRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        string extra = row.Transaction.IsTransfer
            ? "\n\nThis is a transfer, so the matching row in the other account goes too."
            : string.Empty;

        if (!_dialogs.Confirm(
            "Delete transaction",
            $"Delete this transaction?{extra}\n\nThis cannot be undone."))
        {
            return;
        }

        try
        {
            await _register.DeleteAsync(row.Id).ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Delete transaction", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Cycles the "C" column: uncleared to cleared and back.</summary>
    [RelayCommand]
    private async Task ToggleClearedAsync(RegisterRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        if (row.ClearedStatus == ClearedStatus.Reconciled)
        {
            // Reconciled items are settled history. Unpicking one silently would break the
            // agreement with the statement it was balanced against.
            if (!_dialogs.Confirm(
                "Unreconcile",
                "This item was locked in by a completed balance. Unreconcile it?"))
            {
                return;
            }
        }

        ClearedStatus next = row.ClearedStatus == ClearedStatus.Uncleared
            ? ClearedStatus.Cleared
            : ClearedStatus.Uncleared;

        await _register.SetClearedStatusAsync(row.Id, next).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleVoidAsync(RegisterRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        await _register.SetVoidAsync(row.Id, !row.IsVoid).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the import wizard already pointed at this account, so the file step is the only
    /// thing left to do.
    /// </summary>
    [RelayCommand]
    private async Task ImportStatementAsync()
    {
        if (_accountId == 0)
        {
            return;
        }

        ImportWizardViewModel wizard = _services.GetRequired<ImportWizardViewModel>();
        wizard.FixedAccountId = _accountId;

        if (_modals.Show(wizard))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ReconcileAsync()
    {
        if (_accountId == 0)
        {
            return;
        }

        try
        {
            ReconcileSession session = await _reconcile.BeginAsync(_accountId).ConfigureAwait(true);
            var editor = new ReconcileViewModel(_reconcile, session);

            if (_modals.Show(editor))
            {
                await RefreshAsync().ConfigureAwait(true);
            }
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Balance account", ex.Message);
        }
    }

    /// <summary>
    /// Prints the register exactly as it currently reads.
    /// </summary>
    /// <remarks>
    /// The filter is part of what the page means, so it is named on the printout rather than
    /// left implicit — a printed register showing a subset without saying so would mislead
    /// anybody checking it against a statement.
    /// </remarks>
    [RelayCommand]
    private async Task PrintRegisterAsync()
    {
        DateOnly? from = SelectedRange.Months is int months
            ? DateOnly.FromDateTime(DateTime.Today.AddMonths(-months))
            : null;

        var described = new List<string>();

        if (UncategorizedOnly)
        {
            described.Add("Needing a category only");
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            described.Add($"Matching \u201c{SearchText}\u201d");
        }

        PrintCommand.ShowRegister(
            owner: null,
            AccountName,
            [.. Rows],
            from,
            to: null,
            described.Count > 0 ? string.Join("  ·  ", described) : null,
            await PrintColumnsAsync().ConfigureAwait(true));
    }

    /// <summary>
    /// The columns the user chose to print, or null to let the fitter decide.
    /// </summary>
    /// <remarks>
    /// Read at print time rather than cached, so a change made in one register applies to the
    /// next without the page having to be revisited.
    /// </remarks>
    private async Task<IReadOnlyCollection<string>?> PrintColumnsAsync()
    {
        string? stored = await _services.GetRequired<SettingsService>()
            .GetAsync(SettingsService.PrintColumnsKey)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        return [.. stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private RegisterFilter BuildFilter()
    {
        DateOnly? from = SelectedRange.Months is int months
            ? DateOnly.FromDateTime(DateTime.Today.AddMonths(-months))
            : null;

        return new RegisterFilter
        {
            From = from,
            Search = SearchText,
            UncategorizedOnly = UncategorizedOnly,
        };
    }

    partial void OnSearchTextChanged(string? value) => _ = RefreshAsync();

    partial void OnSelectedRangeChanged(RegisterRangeOption value) => _ = RefreshAsync();

    partial void OnUncategorizedOnlyChanged(bool value) => _ = RefreshAsync();

    partial void OnCurrentBalanceChanged(Money value) => OnPropertyChanged(nameof(CurrentBalanceText));

    partial void OnClearedBalanceChanged(Money value) => OnPropertyChanged(nameof(ClearedBalanceText));
}
