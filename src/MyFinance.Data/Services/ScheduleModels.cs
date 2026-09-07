using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Scheduling;

namespace MyFinance.Data.Services;

/// <summary>One row of the bills summary.</summary>
public sealed record BillListItem
{
    public required ScheduledTransaction Schedule { get; init; }

    public required string PayeeName { get; init; }

    public required string AccountName { get; init; }

    public string? CategoryName { get; init; }

    /// <summary>When it is next due, allowing for anything already entered or skipped.</summary>
    public required DateOnly? NextDue { get; init; }

    /// <summary>Pending occurrences whose due date has passed.</summary>
    public required int OverdueCount { get; init; }

    /// <summary>Days since the oldest unpaid occurrence fell due.</summary>
    public required int DaysOverdue { get; init; }

    public int Id => Schedule.Id;

    public Money Amount => Schedule.Amount;

    public bool IsEstimate => Schedule.IsEstimate;

    public PaymentMethod PaymentMethod => Schedule.PaymentMethod;

    public bool IsActive => Schedule.IsActive;

    public bool IsOverdue => OverdueCount > 0;

    /// <summary>Whether it falls due soon enough to be worth flagging.</summary>
    public bool IsDueSoon(DateOnly today) =>
        !IsOverdue && NextDue is DateOnly due && due <= today.AddDays(Schedule.DaysAheadToEnter);

    public string FrequencyText => RecurrenceDescriber.Describe(Schedule.Frequency, Schedule.Interval);

    public string PaymentMethodText => PaymentMethod switch
    {
        PaymentMethod.WriteCheck => "Write Check",
        PaymentMethod.DirectDebit => "Direct Debit",
        PaymentMethod.DirectDeposit => "Direct Deposit",
        PaymentMethod.ElectronicPayment => "Electronic Payment",
        PaymentMethod.Cash => "Cash",
        PaymentMethod.CreditCard => "Credit Card",
        PaymentMethod.Transfer => "Transfer",
        _ => string.Empty,
    };

    /// <summary>
    /// The warning line under an overdue row, worded as the reference books word it.
    /// </summary>
    public string OverdueText => OverdueCount switch
    {
        0 => string.Empty,
        1 => $"This transaction is {DaysOverdue} days overdue.",
        _ => $"This transaction is {DaysOverdue} days overdue ({OverdueCount} occurrences past due).",
    };
}

/// <summary>The bills summary, with the balance projection its footer shows.</summary>
public sealed record BillsSummary
{
    public static BillsSummary Empty { get; } =
        new() { Bills = [], AccountBalances = new Dictionary<int, Money>() };

    public required IReadOnlyList<BillListItem> Bills { get; init; }

    /// <summary>Current balance per account, for the footer's "Current" figure.</summary>
    public required IReadOnlyDictionary<int, Money> AccountBalances { get; init; }

    public int OverdueCount => Bills.Count(b => b.IsOverdue);

    public bool HasEstimates => Bills.Any(b => b.IsEstimate);
}

/// <summary>The editable shape of a scheduled bill or deposit.</summary>
public sealed class ScheduleDraft
{
    public int? Id { get; set; }

    public required int AccountId { get; set; }

    /// <summary>Free text; resolved to an existing payee or used to create one.</summary>
    public string? PayeeName { get; set; }

    public string? Memo { get; set; }

    /// <summary>Signed like a transaction: a bill is negative, a paycheque positive.</summary>
    public Money Amount { get; set; }

    public bool IsEstimate { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;

    public int Interval { get; set; } = 1;

    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public int? SecondDayOfMonth { get; set; }

    public WeekendShift WeekendShift { get; set; }

    public RecurrenceEndKind EndKind { get; set; }

    public DateOnly? EndDate { get; set; }

    public int? OccurrenceCount { get; set; }

    public bool AutoEnter { get; set; }

    public int DaysAheadToEnter { get; set; } = 5;

    public bool IsActive { get; set; } = true;

    /// <summary>Category allocation copied onto every generated transaction.</summary>
    public IReadOnlyList<SplitDraft> Splits { get; set; } = [];

    /// <summary>The recurrence this draft describes.</summary>
    public RecurrenceRule ToRule() => new()
    {
        Frequency = Frequency,
        Interval = Interval,
        StartDate = StartDate,
        SecondDayOfMonth = SecondDayOfMonth,
        WeekendShift = WeekendShift,
        EndKind = EndKind,
        EndDate = EndDate,
        OccurrenceCount = OccurrenceCount,
    };
}

/// <summary>One due date of a series, with what happened to it.</summary>
public sealed record OccurrenceListItem
{
    public required ScheduleOccurrence Occurrence { get; init; }

    public required string PayeeName { get; init; }

    public int Id => Occurrence.Id;

    public DateOnly DueDate => Occurrence.DueDate;

    public ScheduleOccurrenceState State => Occurrence.State;

    public bool IsPending => State == ScheduleOccurrenceState.Pending;
}

/// <summary>What entering a batch of due bills actually did.</summary>
public sealed record EnterResult
{
    public required int Entered { get; init; }

    public required IReadOnlyList<int> TransactionIds { get; init; }

    public Money Total { get; init; }
}
