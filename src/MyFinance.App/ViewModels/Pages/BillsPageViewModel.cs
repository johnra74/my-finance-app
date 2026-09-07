using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Core.Scheduling;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>One row of the bills grid.</summary>
public sealed class BillRowViewModel
{
    public required BillListItem Bill { get; init; }

    public required DateOnly Today { get; init; }

    public int Id => Bill.Id;

    public string PayeeName => Bill.PayeeName;

    public string AccountName => Bill.AccountName;

    public string FrequencyText => Bill.FrequencyText;

    public string PaymentMethodText => Bill.PaymentMethodText;

    public Money Amount => Bill.Amount;

    /// <summary>
    /// The amount with a trailing tilde when the figure is only a forecast, as the reference
    /// books mark them.
    /// </summary>
    public string AmountText =>
        Bill.Amount.ToAccountingString(CultureInfo.CurrentCulture) + (Bill.IsEstimate ? " ~" : string.Empty);

    public string DueDateText => Bill.NextDue is DateOnly due
        ? due.ToString("d", CultureInfo.CurrentCulture)
        : "—";

    public bool IsOverdue => Bill.IsOverdue;

    public bool IsDueSoon => Bill.IsDueSoon(Today);

    /// <summary>The warning line shown under an overdue row.</summary>
    public string OverdueText => Bill.OverdueText;

    public bool IsInactive => !Bill.IsActive;
}

/// <summary>One day in the calendar strip.</summary>
public sealed class CalendarDayViewModel
{
    public required DateOnly Date { get; init; }

    /// <summary>False for the leading and trailing days that pad a month to whole weeks.</summary>
    public required bool IsInMonth { get; init; }

    public required bool HasBill { get; init; }

    public required bool IsToday { get; init; }

    public string DayText => Date.Day.ToString(CultureInfo.CurrentCulture);

    public string? Tooltip { get; init; }
}

/// <summary>One month in the calendar strip.</summary>
public sealed class CalendarMonthViewModel
{
    public required string Header { get; init; }

    public required IReadOnlyList<CalendarDayViewModel> Days { get; init; }
}

/// <summary>
/// The bills summary: what is scheduled, what is overdue, and what the account will hold
/// once it has all been paid.
/// </summary>
public sealed partial class BillsPageViewModel : PageViewModel
{
    /// <summary>How many months the calendar strip shows.</summary>
    private const int CalendarMonths = 5;

    private readonly ScheduleService _schedules;
    private readonly AccountService _accounts;
    private readonly CategoryService _categories;
    private readonly IModalService _modals;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    private BillsSummary _summary = BillsSummary.Empty;

    public BillsPageViewModel(
        ScheduleService schedules,
        AccountService accounts,
        CategoryService categories,
        IModalService modals,
        IDialogService dialogs,
        INavigationService navigation)
    {
        _schedules = schedules;
        _accounts = accounts;
        _categories = categories;
        _modals = modals;
        _dialogs = dialogs;
        _navigation = navigation;
    }

    public override string Title => "Bills summary";

    public override AppSection Section => AppSection.Bills;

    public override HelpTopic HelpTopic => HelpTopic.Bills;

    public ObservableCollection<BillRowViewModel> Bills { get; } = [];

    public ObservableCollection<CalendarMonthViewModel> Calendar { get; } = [];

    [ObservableProperty]
    private BillRowViewModel? _selected;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _showCalendar = true;

    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    private string _balanceCaption = string.Empty;

    [ObservableProperty]
    private Money _currentBalance;

    [ObservableProperty]
    private Money _projectedBalance;

    [ObservableProperty]
    private bool _hasEstimates;

    public string CurrentBalanceText => CurrentBalance.ToAccountingString(CultureInfo.CurrentCulture);

    public string ProjectedBalanceText => ProjectedBalance.ToAccountingString(CultureInfo.CurrentCulture);

    public string TodayText => DateOnly.FromDateTime(DateTime.Today).ToString("d", CultureInfo.CurrentCulture);

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Common tasks",
            Links =
            [
                new TaskLink { Text = "New scheduled transaction", Execute = () => NewCommand.Execute(null) },
                new TaskLink { Text = "Forecast cash flow", Execute = () => _navigation.GoTo<ForecastPageViewModel>() },
                new TaskLink { Text = "Enter everything due", Execute = () => EnterAllDueCommand.Execute(null) },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        bool showInactive = ShowInactive;

        BillsSummary? summary = await RunBusyAsync(
            "Reading the bills",
            (_, token) => _schedules.GetBillsAsync(today, showInactive, cancellationToken: token))
            .ConfigureAwait(true);

        if (summary is null)
        {
            return;
        }

        _summary = summary;

        int? previous = Selected?.Id;

        Bills.Clear();
        foreach (BillListItem bill in _summary.Bills)
        {
            Bills.Add(new BillRowViewModel { Bill = bill, Today = today });
        }

        IsEmpty = Bills.Count == 0;
        HasEstimates = _summary.HasEstimates;
        Selected = Bills.FirstOrDefault(b => b.Id == previous) ?? Bills.FirstOrDefault();

        BuildCalendar(today);
        await UpdateFooterAsync(today).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        ScheduleEditorViewModel editor = ScheduleEditorViewModel.ForNew(
            _schedules,
            await _accounts.GetAllAsync().ConfigureAwait(true),
            await _categories.GetAllAsync().ConfigureAwait(true));

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task EditAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        ScheduledTransaction? schedule = await _schedules.FindAsync(row.Id).ConfigureAwait(true);
        if (schedule is null)
        {
            await RefreshAsync().ConfigureAwait(true);
            return;
        }

        ScheduleEditorViewModel editor = ScheduleEditorViewModel.ForExisting(
            _schedules,
            await _accounts.GetAllAsync().ConfigureAwait(true),
            await _categories.GetAllAsync().ConfigureAwait(true),
            schedule);

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Writes the selected bill's outstanding occurrences into its register.
    /// </summary>
    /// <remarks>
    /// A bill several occurrences behind is entered in full, each on the day it was owed, so
    /// the running balance stays truthful about when the money left rather than dropping the
    /// whole backlog onto today.
    /// </remarks>
    [RelayCommand]
    private async Task EnterInRegisterAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);

        if (row.Bill.OverdueCount > 1 && !_dialogs.Confirm(
            "Enter in register",
            $"\"{row.PayeeName}\" has {row.Bill.OverdueCount} occurrences outstanding.\n\nEnter all of them? Each is recorded on the day it was due."))
        {
            return;
        }

        try
        {
            if (row.Bill.OverdueCount > 1)
            {
                await _schedules.EnterAllDueAsync(row.Id, today).ConfigureAwait(true);
            }
            else
            {
                await _schedules.EnterNextAsync(row.Id, today).ConfigureAwait(true);
            }
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Enter in register", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EnterAllDueAsync()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);

        List<BillRowViewModel> due = [.. Bills.Where(b => b.IsOverdue)];

        if (due.Count == 0)
        {
            _dialogs.ShowInformation("Enter bills", "Nothing is overdue.");
            return;
        }

        if (!_dialogs.Confirm(
            "Enter bills",
            $"Enter every outstanding occurrence of {due.Count} overdue bill{(due.Count == 1 ? string.Empty : "s")}?\n\nEach is recorded on the day it was due."))
        {
            return;
        }

        int entered = 0;

        foreach (BillRowViewModel row in due)
        {
            try
            {
                entered += (await _schedules.EnterAllDueAsync(row.Id, today).ConfigureAwait(true)).Entered;
            }
            catch (BookValidationException ex)
            {
                _dialogs.ShowError("Enter bills", $"{row.PayeeName}: {ex.Message}");
            }
        }

        await RefreshAsync().ConfigureAwait(true);

        _dialogs.ShowInformation(
            "Enter bills",
            $"{entered} transaction{(entered == 1 ? " was" : "s were")} written into the register.");
    }

    [RelayCommand]
    private async Task SkipAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Skip",
            $"Skip the next occurrence of \"{row.PayeeName}\"?\n\nNothing is written to the register and the bill moves on to its following date."))
        {
            return;
        }

        try
        {
            await _schedules.SkipNextAsync(row.Id, DateOnly.FromDateTime(DateTime.Today))
                .ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Skip", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Delete scheduled transaction",
            $"Delete \"{row.PayeeName}\"?\n\nTransactions it has already put in the register stay there — the money did leave the account."))
        {
            return;
        }

        try
        {
            await _schedules.DeleteAsync(row.Id).ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Delete scheduled transaction", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Fills the footer with the selected account's balance now and after everything
    /// scheduled against it has been paid.
    /// </summary>
    private async Task UpdateFooterAsync(DateOnly today)
    {
        BillRowViewModel? row = Selected;

        if (row is null)
        {
            BalanceCaption = string.Empty;
            CurrentBalance = Money.Zero;
            ProjectedBalance = Money.Zero;
            return;
        }

        int accountId = row.Bill.Schedule.AccountId;

        BalanceCaption = $"{row.AccountName} balance:";
        CurrentBalance = _summary.AccountBalances.GetValueOrDefault(accountId);

        // Projected to the end of the calendar strip, so the footer answers the same
        // question the calendar poses: what will be left once all of this has gone out.
        DateOnly horizon = new DateOnly(today.Year, today.Month, 1)
            .AddMonths(CalendarMonths)
            .AddDays(-1);

        CashFlowProjection projection = await _schedules
            .ForecastAsync(accountId, today, horizon)
            .ConfigureAwait(true);

        ProjectedBalance = projection.ClosingBalance;

        OnPropertyChanged(nameof(CurrentBalanceText));
        OnPropertyChanged(nameof(ProjectedBalanceText));
    }

    /// <summary>
    /// Builds the multi-month strip, bolding the days something falls due.
    /// </summary>
    private void BuildCalendar(DateOnly today)
    {
        Calendar.Clear();

        var dueDates = new Dictionary<DateOnly, List<string>>();

        DateOnly firstMonth = new(today.Year, today.Month, 1);
        DateOnly horizon = firstMonth.AddMonths(CalendarMonths).AddDays(-1);

        foreach (BillRowViewModel row in Bills)
        {
            RecurrenceRule rule = ScheduleRule(row.Bill.Schedule);

            foreach (DateOnly date in RecurrenceCalculator.Occurrences(rule, firstMonth, horizon))
            {
                if (!dueDates.TryGetValue(date, out List<string>? names))
                {
                    names = [];
                    dueDates[date] = names;
                }

                names.Add(row.PayeeName);
            }
        }

        for (int offset = 0; offset < CalendarMonths; offset++)
        {
            DateOnly month = firstMonth.AddMonths(offset);
            Calendar.Add(BuildMonth(month, today, dueDates));
        }
    }

    private static CalendarMonthViewModel BuildMonth(
        DateOnly month,
        DateOnly today,
        Dictionary<DateOnly, List<string>> dueDates)
    {
        var days = new List<CalendarDayViewModel>();

        // Weeks start on Sunday, as the reference calendar does.
        int leading = (int)month.DayOfWeek;
        DateOnly cursor = month.AddDays(-leading);

        // Six rows always, so the strip does not change height between months.
        for (int i = 0; i < 42; i++)
        {
            DateOnly date = cursor.AddDays(i);
            bool inMonth = date.Month == month.Month && date.Year == month.Year;

            dueDates.TryGetValue(date, out List<string>? names);

            days.Add(new CalendarDayViewModel
            {
                Date = date,
                IsInMonth = inMonth,
                HasBill = inMonth && names is not null,
                IsToday = date == today,
                Tooltip = names is null ? null : string.Join(Environment.NewLine, names),
            });
        }

        return new CalendarMonthViewModel
        {
            Header = month.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
            Days = days,
        };
    }

    private static RecurrenceRule ScheduleRule(ScheduledTransaction schedule) => new()
    {
        Frequency = schedule.Frequency,
        Interval = Math.Max(1, schedule.Interval),
        StartDate = schedule.StartDate,
        SecondDayOfMonth = schedule.SecondDayOfMonth,
        WeekendShift = schedule.WeekendShift,
        EndKind = schedule.EndKind,
        EndDate = schedule.EndDate,
        OccurrenceCount = schedule.OccurrenceCount,
    };

    partial void OnSelectedChanged(BillRowViewModel? value) =>
        _ = UpdateFooterAsync(DateOnly.FromDateTime(DateTime.Today));

    partial void OnShowInactiveChanged(bool value) => _ = RefreshAsync();
}
