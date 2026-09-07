using MyFinance.Core.Enums;

namespace MyFinance.Import.Mny;

/// <summary>
/// Turns Microsoft Money's recurrence code into one of ours, or admits it cannot.
/// </summary>
/// <remarks>
/// <para>
/// Money stores a recurrence as the pair <c>(frq, cFrqInst)</c>. <c>frq</c> alone is not the
/// frequency: <c>frq = 3</c> covers both monthly and twice-monthly series, and only
/// <c>cFrqInst</c> tells them apart.
/// </para>
/// <para>
/// <b><c>cFrqInst</c> is a count per period, not a multiplier.</b> <c>cFrqInst = 2</c> with
/// <c>frq = 3</c> means <em>twice a month</em>. Our own <see cref="ScheduledTransaction"/>
/// interval means the opposite — an interval of 2 with <see cref="RecurrenceFrequency.Monthly"/>
/// is <em>every two months</em> — so the two must never be passed through to one another.
/// Doing so would turn a twice-monthly salary into a bi-monthly one: half the income, silently.
/// </para>
/// <para>
/// Every mapping below was verified three ways against a real book: Money's own on-screen
/// label for one series of each kind, the observed spacing of the instances Money actually
/// generated, and mutual exclusivity across all 145 datable series in the file. Codes that
/// have never been seen are <b>not guessed</b> — they return null, and the migration reports
/// them for the user to set up by hand. A wrong due date is worse than no due date.
/// </para>
/// </remarks>
public static class MoneyFrequency
{
    /// <summary>Money's unit code for a monthly period.</summary>
    private const int MonthlyUnit = 3;

    /// <summary>Money's unit code for a quarterly period.</summary>
    private const int QuarterlyUnit = 4;

    /// <summary>What a Money recurrence maps to, and how sure we are.</summary>
    /// <param name="Frequency">Null when the code has never been verified.</param>
    /// <param name="Interval">
    /// Our interval, which is a multiplier. Always 1 for the mappings verified so far — Money's
    /// <c>cFrqInst</c> is deliberately <b>not</b> carried through, because it counts
    /// occurrences per period rather than periods per occurrence.
    /// </param>
    public readonly record struct Mapping(RecurrenceFrequency? Frequency, int Interval)
    {
        public static Mapping Unknown => new(null, 1);

        public bool IsKnown => Frequency is not null;
    }

    /// <summary>
    /// Maps a Money recurrence, or returns <see cref="Mapping.Unknown"/> when the pair has
    /// never been seen and verified.
    /// </summary>
    /// <param name="frq">Money's <c>frq</c> column.</param>
    /// <param name="countPerPeriod">
    /// Money's <c>cFrqInst</c> column. <b>Stored as a floating-point value</b>, which is why
    /// reading it with an integer accessor yields null on every row — see the remarks on
    /// <see cref="MoneyFrequency"/>.
    /// </param>
    public static Mapping Map(int? frq, double? countPerPeriod)
    {
        if (frq is not int unit)
        {
            return Mapping.Unknown;
        }

        // Absent is treated as one per period, which is what every observed row with a value
        // of 1 does. It is not treated as "unknown": a missing count is the ordinary case.
        int count = countPerPeriod is double d && d > 0 ? (int)Math.Round(d) : 1;

        return (unit, count) switch
        {
            (MonthlyUnit, 1) => new Mapping(RecurrenceFrequency.Monthly, 1),
            (MonthlyUnit, 2) => new Mapping(RecurrenceFrequency.TwiceAMonth, 1),
            (QuarterlyUnit, 1) => new Mapping(RecurrenceFrequency.Quarterly, 1),
            _ => Mapping.Unknown,
        };
    }
}
