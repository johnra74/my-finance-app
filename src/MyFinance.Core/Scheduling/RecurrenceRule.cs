using MyFinance.Core.Enums;

namespace MyFinance.Core.Scheduling;

/// <summary>
/// How often something repeats, and when it stops.
/// </summary>
/// <remarks>
/// A value with no identity and no database, so the whole of the date arithmetic — which is
/// where scheduling goes wrong — is testable without a book, an account or a transaction.
/// </remarks>
public sealed record RecurrenceRule
{
    public required RecurrenceFrequency Frequency { get; init; }

    /// <summary>Multiplier on the frequency: 2 with Monthly is every second month.</summary>
    public int Interval { get; init; } = 1;

    /// <summary>
    /// The first due date, and the anchor every later one is measured from.
    /// </summary>
    /// <remarks>
    /// Every occurrence is computed from here rather than from the one before it. That is
    /// what keeps a bill due on the 31st landing on the 31st of every long month instead of
    /// creeping backwards to the 28th for the rest of its life after one February.
    /// </remarks>
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// The other day of the month for <see cref="RecurrenceFrequency.TwiceAMonth"/>. Absent,
    /// the two dates are taken as a fortnight apart.
    /// </summary>
    public int? SecondDayOfMonth { get; init; }

    public WeekendShift WeekendShift { get; init; }

    public RecurrenceEndKind EndKind { get; init; }

    public DateOnly? EndDate { get; init; }

    public int? OccurrenceCount { get; init; }

    /// <summary>A one-off on a given date.</summary>
    public static RecurrenceRule Once(DateOnly date) =>
        new() { Frequency = RecurrenceFrequency.Once, StartDate = date };

    /// <summary>Every month on the day <paramref name="start"/> falls on.</summary>
    public static RecurrenceRule Monthly(DateOnly start) =>
        new() { Frequency = RecurrenceFrequency.Monthly, StartDate = start };

    /// <summary>How many months one step of this frequency covers, or zero if it is not monthly.</summary>
    public int MonthsPerStep => Frequency switch
    {
        RecurrenceFrequency.Monthly => 1,
        RecurrenceFrequency.EveryTwoMonths => 2,
        RecurrenceFrequency.Quarterly => 3,
        RecurrenceFrequency.TwiceAYear => 6,
        RecurrenceFrequency.Yearly => 12,
        _ => 0,
    };

    /// <summary>How many days one step covers, or zero if it is not day-based.</summary>
    public int DaysPerStep => Frequency switch
    {
        RecurrenceFrequency.Daily => 1,
        RecurrenceFrequency.Weekly => 7,
        RecurrenceFrequency.EveryTwoWeeks => 14,
        RecurrenceFrequency.EveryFourWeeks => 28,
        _ => 0,
    };
}
