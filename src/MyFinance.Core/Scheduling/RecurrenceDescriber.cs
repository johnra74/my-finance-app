using System.Globalization;
using MyFinance.Core.Enums;

namespace MyFinance.Core.Scheduling;

/// <summary>
/// Puts a recurrence into the words the bills list shows.
/// </summary>
/// <remarks>
/// Deliberately matches how Microsoft Money words these — "Twice a month", "Every three
/// months" — because the user is reading a list they have read for years and a different
/// vocabulary for the same thing is a needless re-learning.
/// </remarks>
public static class RecurrenceDescriber
{
    public static string Describe(RecurrenceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return Describe(rule.Frequency, rule.Interval);
    }

    public static string Describe(RecurrenceFrequency frequency, int interval = 1)
    {
        int every = Math.Max(1, interval);

        if (every == 1)
        {
            return frequency switch
            {
                RecurrenceFrequency.Once => "Only once",
                RecurrenceFrequency.Daily => "Daily",
                RecurrenceFrequency.Weekly => "Weekly",
                RecurrenceFrequency.EveryTwoWeeks => "Every two weeks",
                RecurrenceFrequency.TwiceAMonth => "Twice a month",
                RecurrenceFrequency.EveryFourWeeks => "Every four weeks",
                RecurrenceFrequency.Monthly => "Monthly",
                RecurrenceFrequency.EveryTwoMonths => "Every two months",
                RecurrenceFrequency.Quarterly => "Every three months",
                RecurrenceFrequency.TwiceAYear => "Twice a year",
                RecurrenceFrequency.Yearly => "Yearly",
                _ => "Unknown",
            };
        }

        string unit = frequency switch
        {
            RecurrenceFrequency.Daily => "days",
            RecurrenceFrequency.Weekly => "weeks",
            RecurrenceFrequency.EveryTwoWeeks => "fortnights",
            RecurrenceFrequency.EveryFourWeeks => "four-week periods",
            RecurrenceFrequency.TwiceAMonth => "months, twice each",
            RecurrenceFrequency.Monthly => "months",
            RecurrenceFrequency.EveryTwoMonths => "two-month periods",
            RecurrenceFrequency.Quarterly => "quarters",
            RecurrenceFrequency.TwiceAYear => "half-years",
            RecurrenceFrequency.Yearly => "years",
            _ => "periods",
        };

        return string.Create(CultureInfo.CurrentCulture, $"Every {every} {unit}");
    }

    /// <summary>How the series ends, for the editor's summary line.</summary>
    public static string DescribeEnd(RecurrenceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.EndKind switch
        {
            RecurrenceEndKind.OnDate when rule.EndDate is DateOnly end =>
                string.Create(CultureInfo.CurrentCulture, $"until {end:d MMMM yyyy}"),
            RecurrenceEndKind.AfterOccurrences when rule.OccurrenceCount is int count =>
                string.Create(CultureInfo.CurrentCulture, $"for {count} occurrence{(count == 1 ? string.Empty : "s")}"),
            _ => "with no end date",
        };
    }

    /// <summary>The whole rule in one sentence, e.g. "Monthly from 1 March 2026, with no end date".</summary>
    public static string Summarize(RecurrenceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{Describe(rule)} from {rule.StartDate:d MMMM yyyy}, {DescribeEnd(rule)}");
    }
}
