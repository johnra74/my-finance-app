using MyFinance.Core.Enums;

namespace MyFinance.Core.Scheduling;

/// <summary>
/// Works out when a recurring bill or deposit actually falls due.
/// </summary>
/// <remarks>
/// <para>
/// Every occurrence is derived from the rule's start date and its position in the series,
/// never from the occurrence before it. Iterating month by month looks equivalent and is
/// not: a bill due on the 31st would be pushed to the 28th by one February and then stay
/// there for ever, and a weekend shift would compound until the series had drifted a week.
/// </para>
/// <para>
/// Dates are <see cref="DateOnly"/> throughout, which is also the answer to daylight saving:
/// a due date is a day in a calendar, not an instant, so no clock change can move it.
/// </para>
/// </remarks>
public static class RecurrenceCalculator
{
    /// <summary>
    /// A ceiling on how far a series will be walked, so a malformed rule cannot spin for ever.
    /// </summary>
    public const int MaximumOccurrences = 100_000;

    /// <summary>
    /// The date of one occurrence, or null when the series has ended before it.
    /// </summary>
    /// <param name="rule">The pattern.</param>
    /// <param name="index">Zero-based position in the series.</param>
    /// <returns>The due date, already adjusted for the weekend policy.</returns>
    public static DateOnly? OccurrenceAt(RecurrenceRule rule, int index)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (index < 0)
        {
            return null;
        }

        if (rule.OccurrenceCount is int limit
            && rule.EndKind == RecurrenceEndKind.AfterOccurrences
            && index >= limit)
        {
            return null;
        }

        DateOnly? anchor = AnchorAt(rule, index);

        if (anchor is not DateOnly date)
        {
            return null;
        }

        // The end date is tested against the anchor rather than the shifted date, so a bill
        // whose last occurrence happens to land on a Saturday is not dropped by the shift
        // pushing it past the end.
        if (rule.EndKind == RecurrenceEndKind.OnDate
            && rule.EndDate is DateOnly end
            && date > end)
        {
            return null;
        }

        return ApplyWeekendShift(date, rule.WeekendShift);
    }

    /// <summary>
    /// The unadjusted date of one occurrence — where the pattern puts it before any weekend
    /// policy is applied.
    /// </summary>
    public static DateOnly? AnchorAt(RecurrenceRule rule, int index)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (index < 0)
        {
            return null;
        }

        int interval = Math.Max(1, rule.Interval);

        if (rule.Frequency == RecurrenceFrequency.Once)
        {
            return index == 0 ? rule.StartDate : null;
        }

        if (rule.Frequency == RecurrenceFrequency.TwiceAMonth)
        {
            return TwiceAMonthAt(rule, index, interval);
        }

        if (rule.DaysPerStep is int days and > 0)
        {
            return rule.StartDate.AddDays((long)index * days * interval <= int.MaxValue
                ? index * days * interval
                : int.MaxValue);
        }

        if (rule.MonthsPerStep is int months and > 0)
        {
            return AddMonths(rule.StartDate, index * months * interval);
        }

        return null;
    }

    /// <summary>
    /// Every occurrence between two dates, inclusive.
    /// </summary>
    /// <param name="rule">The pattern.</param>
    /// <param name="from">Earliest due date to return, or null for the start of the series.</param>
    /// <param name="to">Latest due date to return. Required: an unbounded series never ends.</param>
    public static IEnumerable<DateOnly> Occurrences(RecurrenceRule rule, DateOnly? from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(rule);

        for (int index = 0; index < MaximumOccurrences; index++)
        {
            DateOnly? anchor = AnchorAt(rule, index);

            if (anchor is not DateOnly raw)
            {
                yield break;
            }

            if (rule.EndKind == RecurrenceEndKind.AfterOccurrences
                && rule.OccurrenceCount is int limit
                && index >= limit)
            {
                yield break;
            }

            if (rule.EndKind == RecurrenceEndKind.OnDate
                && rule.EndDate is DateOnly end
                && raw > end)
            {
                yield break;
            }

            // Compared on the anchor: a shift can move a date across the horizon in either
            // direction, and stopping on the shifted date would truncate the series early.
            if (raw > to.AddDays(WeekendShiftSlack))
            {
                yield break;
            }

            DateOnly due = ApplyWeekendShift(raw, rule.WeekendShift);

            if (due > to)
            {
                continue;
            }

            if (from is DateOnly start && due < start)
            {
                continue;
            }

            yield return due;
        }
    }

    /// <summary>The first occurrence strictly after a date, or null if the series has ended.</summary>
    public static DateOnly? NextAfter(RecurrenceRule rule, DateOnly after)
    {
        ArgumentNullException.ThrowIfNull(rule);

        for (int index = 0; index < MaximumOccurrences; index++)
        {
            DateOnly? occurrence = OccurrenceAt(rule, index);

            if (occurrence is not DateOnly due)
            {
                // The end-on-date rule can reject one occurrence while later ones remain
                // impossible too, so stopping here is correct for every end condition.
                return null;
            }

            if (due > after)
            {
                return due;
            }
        }

        return null;
    }

    /// <summary>The first occurrence on or after a date — the "next due" the bills list shows.</summary>
    public static DateOnly? NextOnOrAfter(RecurrenceRule rule, DateOnly onOrAfter) =>
        NextAfter(rule, onOrAfter.AddDays(-1));

    /// <summary>How many occurrences fall in a window. Used for "N occurrences past due".</summary>
    public static int CountBetween(RecurrenceRule rule, DateOnly from, DateOnly to) =>
        Occurrences(rule, from, to).Count();

    /// <summary>
    /// Moves a date off a weekend according to the rule's policy.
    /// </summary>
    /// <remarks>
    /// Applied to the computed date and nowhere else, so it never becomes the anchor for the
    /// following occurrence. Bank holidays are not modelled: they differ by country and by
    /// institution, and guessing them wrong is worse than leaving the date alone.
    /// </remarks>
    public static DateOnly ApplyWeekendShift(DateOnly date, WeekendShift shift)
    {
        if (shift == WeekendShift.None)
        {
            return date;
        }

        return date.DayOfWeek switch
        {
            DayOfWeek.Saturday => shift == WeekendShift.PreviousBusinessDay
                ? date.AddDays(-1)
                : date.AddDays(2),
            DayOfWeek.Sunday => shift == WeekendShift.PreviousBusinessDay
                ? date.AddDays(-2)
                : date.AddDays(1),
            _ => date,
        };
    }

    /// <summary>How far a weekend shift can move a date in either direction.</summary>
    private const int WeekendShiftSlack = 2;

    /// <summary>
    /// Adds whole months, clamping to the end of a short month.
    /// </summary>
    /// <remarks>
    /// The 31st of January plus one month is the 28th of February, but because the caller
    /// always adds from the series start rather than from the previous result, the following
    /// month returns to the 31st.
    /// </remarks>
    private static DateOnly AddMonths(DateOnly date, int months)
    {
        int totalMonths = ((date.Year * 12) + date.Month - 1) + months;

        if (totalMonths < 0)
        {
            return DateOnly.MinValue;
        }

        int year = totalMonths / 12;
        int month = (totalMonths % 12) + 1;

        if (year is < 1 or > 9999)
        {
            return year < 1 ? DateOnly.MinValue : DateOnly.MaxValue;
        }

        int day = Math.Min(date.Day, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// The date of one occurrence of a twice-monthly series.
    /// </summary>
    /// <remarks>
    /// Two days of the month, taken in order. When the series starts on the later of the two,
    /// the first occurrence is that later day and the sequence continues from the earlier day
    /// of the following month — which is why the index is shifted by one in that case.
    /// </remarks>
    private static DateOnly? TwiceAMonthAt(RecurrenceRule rule, int index, int interval)
    {
        int firstDay = rule.StartDate.Day;
        int secondDay = rule.SecondDayOfMonth ?? WrapDay(firstDay + 15);

        int earlier = Math.Min(firstDay, secondDay);
        int later = Math.Max(firstDay, secondDay);

        // Both days the same is a degenerate rule; treat it as an ordinary monthly one
        // rather than emitting the same date twice.
        if (earlier == later)
        {
            return AddMonths(rule.StartDate, index * interval);
        }

        int position = rule.StartDate.Day > earlier ? index + 1 : index;
        int monthOffset = position / 2 * interval;
        int day = position % 2 == 0 ? earlier : later;

        DateOnly month = AddMonths(new DateOnly(rule.StartDate.Year, rule.StartDate.Month, 1), monthOffset);

        return new DateOnly(
            month.Year,
            month.Month,
            Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    private static int WrapDay(int day) => day > 31 ? day - 31 : day;
}
