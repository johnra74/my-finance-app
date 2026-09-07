using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.Core.Accounts;
using MyFinance.Core.Primitives;
using MyFinance.Core.Reporting;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>One account on the favourites tile.</summary>
public sealed class FavouriteAccountViewModel
{
    public required AccountSummary Summary { get; init; }

    public int Id => Summary.Id;

    public string Name => Summary.Name;

    public Money Balance => Summary.CurrentBalance;

    public string BalanceText => Balance.ToAccountingString(CultureInfo.CurrentCulture);
}

/// <summary>One bill on the reminders tile.</summary>
public sealed class ReminderViewModel
{
    public required BillListItem Bill { get; init; }

    public int Id => Bill.Id;

    public string PayeeName => Bill.PayeeName;

    public string DueText => Bill.NextDue is DateOnly due
        ? due.ToString("d", CultureInfo.CurrentCulture)
        : "—";

    public Money Amount => Bill.Amount;

    public string AmountText => Amount.ToAccountingString(CultureInfo.CurrentCulture);

    public bool IsOverdue => Bill.IsOverdue;
}

/// <summary>
/// The dashboard: what is worth knowing without asking for it.
/// </summary>
/// <remarks>
/// The spending tile is a horizontal bar chart rather than the pie the original application
/// used. Category names here are long — "Bills : Water and Sewer" — and a pie forces the
/// labels into a legend where the reader has to match colours back to slices. Bars put the
/// name against the length, and stay readable when two categories are close.
/// </remarks>
public sealed partial class HomePageViewModel : PageViewModel
{
    /// <summary>How many categories the spending tile shows before folding the rest.</summary>
    private const int SpendingTileBars = 5;

    private readonly INavigationService _navigation;
    private readonly IServiceProviderAccessor _services;
    private readonly AccountService _accounts;
    private readonly ScheduleService _schedules;
    private readonly ReportService _reports;
    private readonly BudgetService _budgets;

    public HomePageViewModel(
        INavigationService navigation,
        IServiceProviderAccessor services,
        AccountService accounts,
        ScheduleService schedules,
        ReportService reports,
        BudgetService budgets)
    {
        _navigation = navigation;
        _services = services;
        _accounts = accounts;
        _schedules = schedules;
        _reports = reports;
        _budgets = budgets;
    }

    public override string Title => "My Money";

    public override AppSection Section => AppSection.Home;

    public override HelpTopic HelpTopic => HelpTopic.Contents;

    public ObservableCollection<FavouriteAccountViewModel> Favourites { get; } = [];

    public ObservableCollection<ReminderViewModel> Overdue { get; } = [];

    public ObservableCollection<ReminderViewModel> Upcoming { get; } = [];

    public ObservableCollection<BarSlice> Spending { get; } = [];

    public ObservableCollection<SpendingWatchItem> Watched { get; } = [];

    [ObservableProperty]
    private Money _netWorth;

    [ObservableProperty]
    private string _spendingPeriodText = string.Empty;

    [ObservableProperty]
    private Money _spendingTotal;

    [ObservableProperty]
    private bool _hasFavourites;

    [ObservableProperty]
    private bool _hasBills;

    [ObservableProperty]
    private bool _hasSpending;

    [ObservableProperty]
    private bool _hasWatched;

    [ObservableProperty]
    private string _uncategorizedWarning = string.Empty;

    [ObservableProperty]
    private bool _hasUncategorized;

    public string NetWorthText => NetWorth.ToAccountingString(CultureInfo.CurrentCulture);

    public string SpendingTotalText => SpendingTotal.Abs().ToString("C", CultureInfo.CurrentCulture);

    public string TodayText =>
        DateTime.Today.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Common tasks",
            Links =
            [
                new TaskLink { Text = "Account list", Execute = () => _navigation.GoToSection(AppSection.Banking) },
                new TaskLink { Text = "Bills summary", Execute = () => _navigation.GoToSection(AppSection.Bills) },
                new TaskLink { Text = "Reports home", Execute = () => _navigation.GoToSection(AppSection.Reports) },
                new TaskLink { Text = "Budget", Execute = () => _navigation.GoToSection(AppSection.Budget) },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);

        // The tiles each run their own query and one of them totals the whole book, so the
        // dashboard is one of the slower screens despite looking like the lightest.
        await RunBusyAsync(
            "Adding everything up",
            async (_, _) =>
            {
                await LoadAccountsAsync().ConfigureAwait(true);
                await LoadBillsAsync(today).ConfigureAwait(true);
                await LoadSpendingAsync(today).ConfigureAwait(true);
                await LoadWatchListAsync(today).ConfigureAwait(true);
            }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenAccount(FavouriteAccountViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        RegisterPageViewModel page = _services.GetRequired<RegisterPageViewModel>();
        page.SetAccount(row.Id);
        _navigation.GoTo(page);
    }

    [RelayCommand]
    private void GoToBills() => _navigation.GoToSection(AppSection.Bills);

    [RelayCommand]
    private void GoToReports() => _navigation.GoToSection(AppSection.Reports);

    [RelayCommand]
    private void GoToBudget() => _navigation.GoToSection(AppSection.Budget);

    private async Task LoadAccountsAsync()
    {
        AccountListSummary list = await _accounts.GetAccountListAsync().ConfigureAwait(true);

        Favourites.Clear();

        // Favourites when any are marked, otherwise the accounts themselves — an empty tile
        // saying "no favourites have been selected" is the least useful thing a dashboard
        // can show somebody.
        IEnumerable<AccountSummary> shown = list.AllAccounts.Any(a => a.Account.IsFavorite)
            ? list.AllAccounts.Where(a => a.Account.IsFavorite)
            : list.AllAccounts.Take(6);

        foreach (AccountSummary account in shown)
        {
            Favourites.Add(new FavouriteAccountViewModel { Summary = account });
        }

        HasFavourites = Favourites.Count > 0;
        NetWorth = list.Total;
        OnPropertyChanged(nameof(NetWorthText));
    }

    private async Task LoadBillsAsync(DateOnly today)
    {
        BillsSummary bills = await _schedules.GetBillsAsync(today).ConfigureAwait(true);

        Overdue.Clear();
        Upcoming.Clear();

        foreach (BillListItem bill in bills.Bills)
        {
            var row = new ReminderViewModel { Bill = bill };

            if (bill.IsOverdue)
            {
                Overdue.Add(row);
            }
            else if (bill.NextDue is DateOnly due && due <= today.AddDays(30))
            {
                Upcoming.Add(row);
            }
        }

        // Enough to be worth glancing at, not so many that the tile becomes the bills screen.
        Trim(Overdue, 5);
        Trim(Upcoming, 5);

        HasBills = Overdue.Count > 0 || Upcoming.Count > 0;
    }

    private async Task LoadSpendingAsync(DateOnly today)
    {
        DateOnly from = today.AddDays(-29);

        var filter = ReportFilter.Everything with { From = from, To = today };

        ReportOutput output = await _reports
            .RunAsync(ReportKind.SpendingByCategory, filter)
            .ConfigureAwait(true);

        Spending.Clear();

        BarChart chart = ChartGeometry.Bars(output.Grouped!, SpendingTileBars);

        foreach (BarSlice bar in chart.Bars)
        {
            Spending.Add(bar);
        }

        SpendingPeriodText = $"{from:d MMM yyyy} through {today:d MMM yyyy}";
        SpendingTotal = chart.Total;
        HasSpending = !chart.IsEmpty;

        UncategorizedWarning = output.UncategorizedWarning;
        HasUncategorized = output.HasUncategorized;

        OnPropertyChanged(nameof(SpendingTotalText));
    }

    private async Task LoadWatchListAsync(DateOnly today)
    {
        IReadOnlyList<SpendingWatchItem> watched = await _budgets
            .GetWatchListAsync(new DateOnly(today.Year, today.Month, 1))
            .ConfigureAwait(true);

        Watched.Clear();

        foreach (SpendingWatchItem item in watched)
        {
            Watched.Add(item);
        }

        HasWatched = Watched.Count > 0;
    }

    private static void Trim<T>(ObservableCollection<T> items, int keep)
    {
        while (items.Count > keep)
        {
            items.RemoveAt(items.Count - 1);
        }
    }
}
