using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Entities;

/// <summary>
/// A recurring bill or deposit — the rows on the Bills summary screen.
/// </summary>
public class ScheduledTransaction
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public Account? Account { get; set; }

    public int? PayeeId { get; set; }

    public Payee? Payee { get; set; }

    public string? Memo { get; set; }

    /// <summary>Signed like a transaction: a bill is negative, a paycheque positive.</summary>
    public Money Amount { get; set; }

    /// <summary>
    /// True when the amount varies month to month and this is only a forecast. Microsoft
    /// Money marks these with a trailing "~" in the bills list.
    /// </summary>
    public bool IsEstimate { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    // -- Recurrence ---------------------------------------------------------------------

    public RecurrenceFrequency Frequency { get; set; }

    /// <summary>Multiplier on <see cref="Frequency"/>, e.g. 2 with Monthly is every 2 months.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>First due date of the series; the anchor all later dates are computed from.</summary>
    public DateOnly StartDate { get; set; }

    public RecurrenceEndKind EndKind { get; set; }

    public DateOnly? EndDate { get; set; }

    public int? OccurrenceCount { get; set; }

    /// <summary>
    /// Second day-of-month for <see cref="RecurrenceFrequency.TwiceAMonth"/>. A salary paid
    /// on the 15th and the 30th is the case this exists for.
    /// </summary>
    public int? SecondDayOfMonth { get; set; }

    public WeekendShift WeekendShift { get; set; }

    // -- Behaviour ----------------------------------------------------------------------

    /// <summary>Write the transaction into the register automatically when it comes due.</summary>
    public bool AutoEnter { get; set; }

    /// <summary>How many days ahead of the due date to auto-enter or remind.</summary>
    public int DaysAheadToEnter { get; set; } = 5;

    public bool IsActive { get; set; } = true;

    /// <summary>Category allocation applied to each generated transaction.</summary>
    public ICollection<ScheduledTransactionSplit> Splits { get; set; } = [];

    public ICollection<ScheduleOccurrence> Occurrences { get; set; } = [];
}

/// <summary>Category allocation template copied onto each generated transaction.</summary>
public class ScheduledTransactionSplit
{
    public int Id { get; set; }

    public int ScheduledTransactionId { get; set; }

    public ScheduledTransaction? ScheduledTransaction { get; set; }

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }

    public Money Amount { get; set; }

    public string? Memo { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// A single due date of a series, materialized so that "9 occurrences past due" is a query
/// against recorded state rather than a recomputation that can disagree with itself.
/// </summary>
public class ScheduleOccurrence
{
    public int Id { get; set; }

    public int ScheduledTransactionId { get; set; }

    public ScheduledTransaction? ScheduledTransaction { get; set; }

    public DateOnly DueDate { get; set; }

    public ScheduleOccurrenceState State { get; set; }

    /// <summary>The register row created when this occurrence was entered.</summary>
    public int? TransactionId { get; set; }

    public Transaction? Transaction { get; set; }

    /// <summary>Overrides the series amount when a single instance differed.</summary>
    public Money? ActualAmount { get; set; }

    public DateTimeOffset? ResolvedUtc { get; set; }
}
