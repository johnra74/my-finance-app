using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Core.Scheduling;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>One dated point on the projected balance line.</summary>
public sealed class ForecastRowViewModel
{
    public required ForecastPoint Point { get; init; }

    public DateOnly Date => Point.Date;

    public Money Balance => Point.Balance;

    public Money Change => Point.Change;

    public string DateText => Date.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);

    public string ChangeText =>
        Point.Change.IsZero ? string.Empty : Point.Change.ToAccountingString(CultureInfo.CurrentCulture);

    public string BalanceText => Balance.ToAccountingString(CultureInfo.CurrentCulture);

    /// <summary>What moved on this day, for the detail column.</summary>
    public string Detail => string.Join(", ", Point.Events.Select(e => e.Description));

    public bool IsNegative => Balance.IsNegative;

    /// <summary>True when every movement that day has already happened.</summary>
    public bool IsHistory => Point.HasEvents && Point.Events.All(e => e.IsAlreadyRecorded);

    public bool IncludesEstimate => Point.Events.Any(e => e.IsEstimate);
}

/// <summary>How far forward to project.</summary>
/// <param name="Text">How it reads in the picker.</param>
/// <param name="Months">Length of the window.</param>
public readonly record struct ForecastRange(string Text, int Months);

/// <summary>
/// Projects an account's balance forward over everything scheduled against it.
/// </summary>
/// <remarks>
/// The question this answers is not "what do I owe" but "will there be enough when it is
/// taken". The lowest point of the line matters more than where it ends: a large bill early
/// in the month followed by a salary can finish comfortably while going overdrawn in between.
/// </remarks>
public sealed partial class ForecastPageViewModel : PageViewModel
{
    private readonly ScheduleService _schedules;
    private readonly AccountService _accounts;
    private readonly INavigationService _navigation;

    public ForecastPageViewModel(
        ScheduleService schedules,
        AccountService accounts,
        INavigationService navigation)
    {
        _schedules = schedules;
        _accounts = accounts;
        _navigation = navigation;

        Ranges =
        [
            new ForecastRange("Next 3 months", 3),
            new ForecastRange("Next 6 months", 6),
            new ForecastRange("Next 12 months", 12),
        ];

        _selectedRange = Ranges[0];
    }

    public override string Title => "Cash flow forecast";

    public override AppSection Section => AppSection.Bills;

    public override HelpTopic HelpTopic => HelpTopic.Bills;

    public ObservableCollection<ForecastRowViewModel> Rows { get; } = [];

    public ObservableCollection<Account> Accounts { get; } = [];

    public IReadOnlyList<ForecastRange> Ranges { get; }

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private ForecastRange _selectedRange;

    [ObservableProperty]
    private Money _openingBalance;

    [ObservableProperty]
    private Money _closingBalance;

    [ObservableProperty]
    private Money _lowestBalance;

    [ObservableProperty]
    private string _lowestBalanceText = string.Empty;

    [ObservableProperty]
    private bool _goesNegative;

    [ObservableProperty]
    private string _warning = string.Empty;

    [ObservableProperty]
    private bool _includesEstimates;

    [ObservableProperty]
    private bool _isEmpty = true;

    public string OpeningBalanceText => OpeningBalance.ToAccountingString(CultureInfo.CurrentCulture);

    public string ClosingBalanceText => ClosingBalance.ToAccountingString(CultureInfo.CurrentCulture);

    public string AccountCaption => SelectedAccount?.Name ?? "All accounts";

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Common tasks",
            Links =
            [
                new TaskLink { Text = "Bills summary", Execute = () => _navigation.GoToSection(AppSection.Bills) },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Accounts.Count == 0)
        {
            foreach (Account account in await _accounts.GetAllAsync().ConfigureAwait(true))
            {
                Accounts.Add(account);
            }
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly horizon = today.AddMonths(SelectedRange.Months);
        int? accountId = SelectedAccount?.Id;

        CashFlowProjection? projection = await RunBusyAsync(
            "Projecting the balance",
            (_, token) => _schedules.ForecastAsync(accountId, today, horizon, cancellationToken: token))
            .ConfigureAwait(true);

        if (projection is null)
        {
            return;
        }

        Rows.Clear();
        foreach (ForecastPoint point in projection.Points)
        {
            Rows.Add(new ForecastRowViewModel { Point = point });
        }

        OpeningBalance = projection.OpeningBalance;
        ClosingBalance = projection.ClosingBalance;
        LowestBalance = projection.LowestBalance;
        GoesNegative = projection.GoesNegative;
        IncludesEstimates = projection.IncludesEstimates;
        IsEmpty = projection.Events.Count == 0;

        LowestBalanceText = projection.LowestBalanceDate is DateOnly low
            ? $"Lowest point {projection.LowestBalance.ToAccountingString(CultureInfo.CurrentCulture)} on {low:d MMMM yyyy}"
            : string.Empty;

        Warning = projection.FirstNegativeDate is DateOnly negative
            ? $"On this projection {AccountCaption} goes overdrawn on {negative:d MMMM yyyy}."
            : string.Empty;

        OnPropertyChanged(nameof(OpeningBalanceText));
        OnPropertyChanged(nameof(ClosingBalanceText));
        OnPropertyChanged(nameof(AccountCaption));
    }

    partial void OnSelectedAccountChanged(Account? value) => _ = RefreshAsync();

    partial void OnSelectedRangeChanged(ForecastRange value) => _ = RefreshAsync();
}
