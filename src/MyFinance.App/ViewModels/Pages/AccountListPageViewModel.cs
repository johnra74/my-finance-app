using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Accounts;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>One account row on the Banking screen.</summary>
public sealed class AccountRowViewModel
{
    public required AccountSummary Summary { get; init; }

    public int Id => Summary.Id;

    public string Name => Summary.Name;

    public string? Institution => Summary.Account.Institution;

    public string? AccountNumberMasked => Summary.Account.AccountNumberMasked;

    public Money CurrentBalance => Summary.CurrentBalance;

    public Money ClearedBalance => Summary.ClearedBalance;

    public bool IsClosed => Summary.IsClosed;

    public bool IsReadOnly => Summary.Account.IsReadOnly;

    public int UncategorizedCount => Summary.UncategorizedCount;

    public bool HasUncategorized => UncategorizedCount > 0;

    public string UncategorizedText =>
        $"{UncategorizedCount} without a category";

    public string LastUpdatedText => Summary.Account.LastUpdatedOn is DateTimeOffset on
        ? on.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
        : "—";

    public string TransactionCountText =>
        $"{Summary.TransactionCount} transaction{(Summary.TransactionCount == 1 ? string.Empty : "s")}";
}

/// <summary>A heading with its accounts and subtotal.</summary>
public sealed class AccountGroupViewModel
{
    public required string Header { get; init; }

    public required IReadOnlyList<AccountRowViewModel> Accounts { get; init; }

    public required Money Subtotal { get; init; }
}

/// <summary>
/// The Banking screen: every account, grouped and subtotalled, and the entry point to the
/// register.
/// </summary>
public sealed partial class AccountListPageViewModel : PageViewModel
{
    private readonly AccountService _accounts;
    private readonly INavigationService _navigation;
    private readonly IModalService _modals;
    private readonly IDialogService _dialogs;
    private readonly IServiceProviderAccessor _services;

    public AccountListPageViewModel(
        AccountService accounts,
        INavigationService navigation,
        IModalService modals,
        IDialogService dialogs,
        IServiceProviderAccessor services)
    {
        _accounts = accounts;
        _navigation = navigation;
        _modals = modals;
        _dialogs = dialogs;
        _services = services;
    }

    public override string Title => "Account list";

    public override AppSection Section => AppSection.Banking;

    public override HelpTopic HelpTopic => HelpTopic.AccountsAndRegister;

    public ObservableCollection<AccountGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    private Money _total;

    [ObservableProperty]
    private bool _showClosedAccounts;

    [ObservableProperty]
    private AccountRowViewModel? _selectedAccount;

    [ObservableProperty]
    private bool _isEmpty = true;

    public string TotalText => Total.ToAccountingString(CultureInfo.CurrentCulture);

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Common tasks",
            Links =
            [
                new TaskLink { Text = "Add a new account", Execute = () => NewAccountCommand.Execute(null) },
                new TaskLink { Text = "Import a statement…", Execute = () => ImportStatementCommand.Execute(null) },
                new TaskLink { Text = "Import history…", Execute = () => ImportHistoryCommand.Execute(null) },
                new TaskLink { Text = "Bring across a Money file…", Execute = () => MigrateCommand.Execute(null) },
                new TaskLink { Text = "Categories", Execute = () => _navigation.GoTo<CategoriesPageViewModel>() },
                new TaskLink { Text = "Payees", Execute = () => _navigation.GoTo<PayeesPageViewModel>() },
                new TaskLink { Text = "Categorization rules", Execute = () => _navigation.GoTo<RulesPageViewModel>() },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        bool showClosed = ShowClosedAccounts;

        // Balances are summed over every transaction in the book, so this is not the cheap
        // query it looks like.
        AccountListSummary? list = await RunBusyAsync(
            "Adding up the accounts",
            (_, token) => _accounts.GetAccountListAsync(showClosed, token)).ConfigureAwait(true);

        if (list is null)
        {
            return;
        }

        int? previous = SelectedAccount?.Id;

        Groups.Clear();

        foreach (AccountGroupSummary group in list.Groups)
        {
            Groups.Add(new AccountGroupViewModel
            {
                Header = group.Header,
                Subtotal = group.Subtotal,
                Accounts = [.. group.Accounts.Select(a => new AccountRowViewModel { Summary = a })],
            });
        }

        Total = list.Total;
        IsEmpty = list.AccountCount == 0;

        SelectedAccount = Groups
            .SelectMany(g => g.Accounts)
            .FirstOrDefault(a => a.Id == previous);

        OnPropertyChanged(nameof(TotalText));
    }

    /// <summary>
    /// Opens the import wizard, optionally aimed at the selected account.
    /// </summary>
    [RelayCommand]
    private async Task ImportStatementAsync(AccountRowViewModel? row)
    {
        ImportWizardViewModel wizard = _services.GetRequired<ImportWizardViewModel>();

        if (row is not null)
        {
            wizard.FixedAccountId = row.Id;
        }

        if (_modals.Show(wizard))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Brings a Microsoft Money book across.
    /// </summary>
    /// <remarks>
    /// Reachable from the account list because that is the screen it fills, and because
    /// the only book it can write into is an empty one — which is exactly what the user is
    /// looking at when they first get here.
    /// </remarks>
    [RelayCommand]
    private async Task MigrateAsync()
    {
        MigrationWizardViewModel wizard = _services.GetRequired<MigrationWizardViewModel>();

        if (_modals.Show(wizard))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ImportHistoryAsync()
    {
        ImportHistoryViewModel history = _services.GetRequired<ImportHistoryViewModel>();
        await history.RefreshAsync().ConfigureAwait(true);

        // Undoing an import changes balances, so the list has to be reloaded afterwards.
        if (_modals.Show(history))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task NewAccountAsync()
    {
        AccountEditorViewModel editor = AccountEditorViewModel.ForNew(_accounts);

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task EditAccountAsync(AccountRowViewModel? row)
    {
        row ??= SelectedAccount;
        if (row is null)
        {
            return;
        }

        Account? account = await _accounts.FindAsync(row.Id).ConfigureAwait(true);
        if (account is null)
        {
            await RefreshAsync().ConfigureAwait(true);
            return;
        }

        AccountEditorViewModel editor = AccountEditorViewModel.ForExisting(_accounts, account);

        if (await _accounts.CountTransactionsAsync(row.Id).ConfigureAwait(true) > 0)
        {
            editor.FreezeType();
        }

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Opens the account's register — the double-click action on a row.</summary>
    [RelayCommand]
    private void OpenRegister(AccountRowViewModel? row)
    {
        row ??= SelectedAccount;
        if (row is null)
        {
            return;
        }

        RegisterPageViewModel page = _services.GetRequired<RegisterPageViewModel>();
        page.SetAccount(row.Id);
        _navigation.GoTo(page);
    }

    [RelayCommand]
    private async Task ToggleClosedAsync(AccountRowViewModel? row)
    {
        row ??= SelectedAccount;
        if (row is null)
        {
            return;
        }

        bool closing = !row.IsClosed;

        if (closing && !_dialogs.Confirm(
            "Close account",
            $"Close \"{row.Name}\"?\n\nIts history stays in the books and keeps counting towards reports. It simply stops appearing in the account list and in pickers."))
        {
            return;
        }

        await _accounts.SetClosedAsync(row.Id, closing).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(AccountRowViewModel? row)
    {
        row ??= SelectedAccount;
        if (row is null)
        {
            return;
        }

        int count = await _accounts.CountTransactionsAsync(row.Id).ConfigureAwait(true);

        // Says exactly how much history is about to go, because this cannot be undone and
        // there is no way back short of restoring a backup.
        string warning = count == 0
            ? $"Delete \"{row.Name}\"?"
            : $"Delete \"{row.Name}\" and all {count} of its transactions?\n\nThe other half of any transfer will be removed from the other account too. This cannot be undone.\n\nIf you only want it out of the way, close it instead.";

        if (!_dialogs.Confirm("Delete account", warning))
        {
            return;
        }

        try
        {
            await _accounts.DeleteAsync(row.Id).ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Delete account", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnShowClosedAccountsChanged(bool value) => _ = RefreshAsync();

    partial void OnTotalChanged(Money value) => OnPropertyChanged(nameof(TotalText));
}
