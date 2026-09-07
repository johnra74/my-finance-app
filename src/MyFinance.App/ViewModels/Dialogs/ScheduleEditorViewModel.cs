using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Scheduling;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>Whether the scheduled amount leaves the account or arrives in it.</summary>
public enum ScheduleDirection
{
    /// <summary>A bill.</summary>
    Payment = 0,

    /// <summary>A paycheque or other regular deposit.</summary>
    Deposit = 1,
}

/// <summary>Creates or edits one recurring bill or deposit.</summary>
public sealed partial class ScheduleEditorViewModel : DialogViewModel
{
    private readonly ScheduleService _schedules;
    private readonly int? _id;

    private ScheduleEditorViewModel(
        ScheduleService schedules,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<CategoryListItem> categories,
        ScheduledTransaction? existing)
    {
        _schedules = schedules;
        _id = existing?.Id;

        Accounts = accounts;
        Categories = categories;

        Frequencies =
        [
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.Monthly, "Monthly"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.TwiceAMonth, "Twice a month"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.EveryTwoWeeks, "Every two weeks"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.Weekly, "Weekly"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.EveryFourWeeks, "Every four weeks"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.EveryTwoMonths, "Every two months"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.Quarterly, "Every three months"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.TwiceAYear, "Twice a year"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.Yearly, "Yearly"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.Daily, "Daily"),
            new Choice<RecurrenceFrequency>(RecurrenceFrequency.Once, "Only once"),
        ];

        Methods =
        [
            new Choice<PaymentMethod>(PaymentMethod.WriteCheck, "Write Check"),
            new Choice<PaymentMethod>(PaymentMethod.DirectDebit, "Direct Debit"),
            new Choice<PaymentMethod>(PaymentMethod.DirectDeposit, "Direct Deposit"),
            new Choice<PaymentMethod>(PaymentMethod.ElectronicPayment, "Electronic Payment"),
            new Choice<PaymentMethod>(PaymentMethod.CreditCard, "Credit Card"),
            new Choice<PaymentMethod>(PaymentMethod.Cash, "Cash"),
            new Choice<PaymentMethod>(PaymentMethod.Transfer, "Transfer"),
            new Choice<PaymentMethod>(PaymentMethod.Unspecified, "Not specified"),
        ];

        WeekendShifts =
        [
            new Choice<WeekendShift>(WeekendShift.None, "Leave it on the weekend"),
            new Choice<WeekendShift>(WeekendShift.PreviousBusinessDay, "Move to the Friday before"),
            new Choice<WeekendShift>(WeekendShift.NextBusinessDay, "Move to the Monday after"),
        ];

        Endings =
        [
            new Choice<RecurrenceEndKind>(RecurrenceEndKind.Never, "Carries on indefinitely"),
            new Choice<RecurrenceEndKind>(RecurrenceEndKind.OnDate, "Ends on a date"),
            new Choice<RecurrenceEndKind>(RecurrenceEndKind.AfterOccurrences, "Ends after a number of times"),
        ];

        _selectedFrequency = Frequencies[0];
        _selectedMethod = Methods[0];
        _selectedWeekendShift = WeekendShifts[0];
        _selectedEnding = Endings[0];
        _selectedAccount = accounts.FirstOrDefault();
        _startDate = DateTime.Today;

        if (existing is null)
        {
            return;
        }

        _payeeName = existing.Payee?.Name;
        _memo = existing.Memo;
        _direction = existing.Amount.IsPositive ? ScheduleDirection.Deposit : ScheduleDirection.Payment;
        _amountText = existing.Amount.Abs().ToString("N", CultureInfo.CurrentCulture);
        _isEstimate = existing.IsEstimate;
        _startDate = existing.StartDate.ToDateTime(TimeOnly.MinValue);
        _interval = Math.Max(1, existing.Interval);
        _secondDayOfMonth = existing.SecondDayOfMonth;
        _autoEnter = existing.AutoEnter;
        _daysAheadToEnter = existing.DaysAheadToEnter;
        _isActive = existing.IsActive;
        _endDate = existing.EndDate?.ToDateTime(TimeOnly.MinValue);
        _occurrenceCount = existing.OccurrenceCount;

        _selectedAccount = accounts.FirstOrDefault(a => a.Id == existing.AccountId) ?? _selectedAccount;
        _selectedFrequency = Pick(Frequencies, existing.Frequency);
        _selectedMethod = Pick(Methods, existing.PaymentMethod);
        _selectedWeekendShift = Pick(WeekendShifts, existing.WeekendShift);
        _selectedEnding = Pick(Endings, existing.EndKind);

        if (existing.Splits.Count == 1)
        {
            _selectedCategory = categories.FirstOrDefault(c => c.Id == existing.Splits.First().CategoryId);
        }
    }

    public static ScheduleEditorViewModel ForNew(
        ScheduleService schedules,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<CategoryListItem> categories) =>
        new(schedules, accounts, categories, null);

    public static ScheduleEditorViewModel ForExisting(
        ScheduleService schedules,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<CategoryListItem> categories,
        ScheduledTransaction existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return new ScheduleEditorViewModel(schedules, accounts, categories, existing);
    }

    public override string Title => _id is null ? "New scheduled transaction" : "Edit scheduled transaction";

    public IReadOnlyList<Account> Accounts { get; }

    public IReadOnlyList<CategoryListItem> Categories { get; }

    public IReadOnlyList<Choice<RecurrenceFrequency>> Frequencies { get; }

    public IReadOnlyList<Choice<PaymentMethod>> Methods { get; }

    public IReadOnlyList<Choice<WeekendShift>> WeekendShifts { get; }

    public IReadOnlyList<Choice<RecurrenceEndKind>> Endings { get; }

    public IReadOnlyList<ScheduleDirection> Directions { get; } =
        [ScheduleDirection.Payment, ScheduleDirection.Deposit];

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private string? _payeeName;

    [ObservableProperty]
    private string? _memo;

    [ObservableProperty]
    private ScheduleDirection _direction = ScheduleDirection.Payment;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private bool _isEstimate;

    [ObservableProperty]
    private CategoryListItem? _selectedCategory;

    [ObservableProperty]
    private Choice<PaymentMethod> _selectedMethod;

    [ObservableProperty]
    private Choice<RecurrenceFrequency> _selectedFrequency;

    [ObservableProperty]
    private int _interval = 1;

    [ObservableProperty]
    private DateTime _startDate;

    [ObservableProperty]
    private int? _secondDayOfMonth;

    [ObservableProperty]
    private Choice<WeekendShift> _selectedWeekendShift;

    [ObservableProperty]
    private Choice<RecurrenceEndKind> _selectedEnding;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private int? _occurrenceCount;

    [ObservableProperty]
    private bool _autoEnter;

    [ObservableProperty]
    private int _daysAheadToEnter = 5;

    [ObservableProperty]
    private bool _isActive = true;

    /// <summary>The second day only matters for a twice-monthly series.</summary>
    public bool NeedsSecondDay => SelectedFrequency.Value == RecurrenceFrequency.TwiceAMonth;

    public bool NeedsEndDate => SelectedEnding.Value == RecurrenceEndKind.OnDate;

    public bool NeedsOccurrenceCount => SelectedEnding.Value == RecurrenceEndKind.AfterOccurrences;

    /// <summary>The whole pattern in one sentence, so the editor can show what it will do.</summary>
    public string RecurrenceSummary => RecurrenceDescriber.Summarize(BuildRule());

    /// <summary>The next few dates the pattern produces — the real check that it is right.</summary>
    public string NextDatesPreview
    {
        get
        {
            List<DateOnly> dates =
            [
                .. RecurrenceCalculator
                    .Occurrences(BuildRule(), null, DateOnly.FromDateTime(StartDate).AddYears(3))
                    .Take(5)
            ];

            return dates.Count == 0
                ? "This pattern produces no dates."
                : "Next: " + string.Join(", ", dates.Select(d => d.ToString("d MMM yyyy", CultureInfo.CurrentCulture)));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (SelectedAccount is null)
        {
            ErrorMessage = "Choose which account this comes out of.";
            return;
        }

        if (!Money.TryParse(AmountText, CultureInfo.CurrentCulture, out Money entered)
            && !string.IsNullOrWhiteSpace(AmountText))
        {
            ErrorMessage = "The amount is not a number.";
            return;
        }

        // The direction control owns the sign, so a stray minus typed into the box cannot
        // turn a paycheque into a bill.
        Money amount = Direction == ScheduleDirection.Payment
            ? entered.Abs().Negated()
            : entered.Abs();

        var draft = new ScheduleDraft
        {
            Id = _id,
            AccountId = SelectedAccount.Id,
            PayeeName = PayeeName,
            Memo = Memo,
            Amount = amount,
            IsEstimate = IsEstimate,
            PaymentMethod = SelectedMethod.Value,
            Frequency = SelectedFrequency.Value,
            Interval = Math.Max(1, Interval),
            StartDate = DateOnly.FromDateTime(StartDate),
            SecondDayOfMonth = NeedsSecondDay ? SecondDayOfMonth : null,
            WeekendShift = SelectedWeekendShift.Value,
            EndKind = SelectedEnding.Value,
            EndDate = NeedsEndDate && EndDate is DateTime end ? DateOnly.FromDateTime(end) : null,
            OccurrenceCount = NeedsOccurrenceCount ? OccurrenceCount : null,
            AutoEnter = AutoEnter,
            DaysAheadToEnter = Math.Max(0, DaysAheadToEnter),
            IsActive = IsActive,
            Splits = SelectedCategory is null
                ? []
                : [new SplitDraft { CategoryId = SelectedCategory.Id, Amount = amount }],
        };

        IsBusy = true;

        try
        {
            if (_id is null)
            {
                await _schedules.CreateAsync(draft).ConfigureAwait(true);
            }
            else
            {
                await _schedules.UpdateAsync(draft).ConfigureAwait(true);
            }

            Close(true);
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private RecurrenceRule BuildRule() => new()
    {
        Frequency = SelectedFrequency.Value,
        Interval = Math.Max(1, Interval),
        StartDate = DateOnly.FromDateTime(StartDate),
        SecondDayOfMonth = NeedsSecondDay ? SecondDayOfMonth : null,
        WeekendShift = SelectedWeekendShift.Value,
        EndKind = SelectedEnding.Value,
        EndDate = NeedsEndDate && EndDate is DateTime end ? DateOnly.FromDateTime(end) : null,
        OccurrenceCount = NeedsOccurrenceCount ? OccurrenceCount : null,
    };

    private static Choice<T> Pick<T>(IReadOnlyList<Choice<T>> options, T value)
        where T : struct, Enum =>
        options.FirstOrDefault(o => o.Value.Equals(value), options[0]);

    private void RefreshPattern()
    {
        OnPropertyChanged(nameof(NeedsSecondDay));
        OnPropertyChanged(nameof(NeedsEndDate));
        OnPropertyChanged(nameof(NeedsOccurrenceCount));
        OnPropertyChanged(nameof(RecurrenceSummary));
        OnPropertyChanged(nameof(NextDatesPreview));
    }

    partial void OnSelectedFrequencyChanged(Choice<RecurrenceFrequency> value) => RefreshPattern();

    partial void OnSelectedEndingChanged(Choice<RecurrenceEndKind> value) => RefreshPattern();

    partial void OnStartDateChanged(DateTime value) => RefreshPattern();

    partial void OnIntervalChanged(int value) => RefreshPattern();

    partial void OnSecondDayOfMonthChanged(int? value) => RefreshPattern();

    partial void OnSelectedWeekendShiftChanged(Choice<WeekendShift> value) => RefreshPattern();

    partial void OnEndDateChanged(DateTime? value) => RefreshPattern();

    partial void OnOccurrenceCountChanged(int? value) => RefreshPattern();
}
