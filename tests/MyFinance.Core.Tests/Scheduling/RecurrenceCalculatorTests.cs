using MyFinance.Core.Enums;
using MyFinance.Core.Scheduling;

namespace MyFinance.Core.Tests.Scheduling;

public sealed class RecurrenceCalculatorTests
{
    [Fact]
    public void A_one_off_happens_exactly_once()
    {
        RecurrenceRule rule = RecurrenceRule.Once(new DateOnly(2026, 3, 2));

        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2026, 3, 2));
        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBeNull();
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Daily, 1, "2026-03-03")]
    [InlineData(RecurrenceFrequency.Weekly, 1, "2026-03-09")]
    [InlineData(RecurrenceFrequency.EveryTwoWeeks, 1, "2026-03-16")]
    [InlineData(RecurrenceFrequency.EveryFourWeeks, 1, "2026-03-30")]
    [InlineData(RecurrenceFrequency.Monthly, 1, "2026-04-02")]
    [InlineData(RecurrenceFrequency.EveryTwoMonths, 1, "2026-05-02")]
    [InlineData(RecurrenceFrequency.Quarterly, 1, "2026-06-02")]
    [InlineData(RecurrenceFrequency.TwiceAYear, 1, "2026-09-02")]
    [InlineData(RecurrenceFrequency.Yearly, 1, "2027-03-02")]
    public void Each_frequency_steps_by_its_own_period(
        RecurrenceFrequency frequency,
        int interval,
        string expected)
    {
        var rule = new RecurrenceRule
        {
            Frequency = frequency,
            Interval = interval,
            StartDate = new DateOnly(2026, 3, 2),
        };

        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(TestDates.Parse(expected));
    }

    [Fact]
    public void An_interval_multiplies_the_period()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            Interval = 3,
            StartDate = new DateOnly(2026, 1, 15),
        };

        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2026, 4, 15));
        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 7, 15));
    }

    [Fact]
    public void A_bill_due_on_the_thirty_first_returns_to_the_thirty_first_after_a_short_month()
    {
        // The reason every date is measured from the start rather than from the one before.
        // Stepping month by month would clamp to 28 in February and stay there for ever.
        RecurrenceRule rule = RecurrenceRule.Monthly(new DateOnly(2026, 1, 31));

        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2026, 1, 31));
        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2026, 2, 28));
        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 3, 31));
        RecurrenceCalculator.OccurrenceAt(rule, 3).ShouldBe(new DateOnly(2026, 4, 30));
        RecurrenceCalculator.OccurrenceAt(rule, 4).ShouldBe(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public void February_gets_its_extra_day_in_a_leap_year()
    {
        RecurrenceRule rule = RecurrenceRule.Monthly(new DateOnly(2024, 1, 31));

        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2024, 2, 29));
    }

    [Fact]
    public void A_yearly_bill_on_the_leap_day_falls_back_to_the_twenty_eighth()
    {
        RecurrenceRule rule = new()
        {
            Frequency = RecurrenceFrequency.Yearly,
            StartDate = new DateOnly(2024, 2, 29),
        };

        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2024, 2, 29));
        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2025, 2, 28));

        // And returns to the 29th at the next leap year rather than staying on the 28th.
        RecurrenceCalculator.OccurrenceAt(rule, 4).ShouldBe(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void A_clock_change_cannot_move_a_due_date()
    {
        // Dates here are days in a calendar, not instants, so there is no zone for daylight
        // saving to shift. This asserts the property rather than the implementation.
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Daily,
            StartDate = new DateOnly(2026, 3, 6),
        };

        // The United States moves its clocks on 8 March 2026 and Europe on 29 March.
        List<DateOnly> dates = [.. RecurrenceCalculator.Occurrences(rule, null, new DateOnly(2026, 3, 12))];

        dates.ShouldBe(
        [
            new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 7), new DateOnly(2026, 3, 8),
            new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 11),
            new DateOnly(2026, 3, 12),
        ]);
    }
}

public sealed class TwiceAMonthTests
{
    [Fact]
    public void The_two_days_alternate_through_the_months()
    {
        // The shape a twice-a-month salary deposit uses.
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.TwiceAMonth,
            StartDate = new DateOnly(2026, 4, 15),
            SecondDayOfMonth = 30,
        };

        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2026, 4, 15));
        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2026, 4, 30));
        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 5, 15));
        RecurrenceCalculator.OccurrenceAt(rule, 3).ShouldBe(new DateOnly(2026, 5, 30));
    }

    [Fact]
    public void Starting_on_the_later_day_continues_from_the_earlier_day_next_month()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.TwiceAMonth,
            StartDate = new DateOnly(2026, 4, 30),
            SecondDayOfMonth = 15,
        };

        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2026, 4, 30));
        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2026, 5, 15));
        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 5, 30));
        RecurrenceCalculator.OccurrenceAt(rule, 3).ShouldBe(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public void The_second_day_defaults_to_a_fortnight_later()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.TwiceAMonth,
            StartDate = new DateOnly(2026, 4, 1),
        };

        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2026, 4, 1));
        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2026, 4, 16));
        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public void The_thirty_first_is_clamped_in_a_short_month()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.TwiceAMonth,
            StartDate = new DateOnly(2026, 1, 15),
            SecondDayOfMonth = 31,
        };

        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2026, 1, 31));
        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 2, 15));
        RecurrenceCalculator.OccurrenceAt(rule, 3).ShouldBe(new DateOnly(2026, 2, 28));
        RecurrenceCalculator.OccurrenceAt(rule, 4).ShouldBe(new DateOnly(2026, 3, 15));
        RecurrenceCalculator.OccurrenceAt(rule, 5).ShouldBe(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void Two_identical_days_degrade_to_monthly_rather_than_repeating_a_date()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.TwiceAMonth,
            StartDate = new DateOnly(2026, 4, 15),
            SecondDayOfMonth = 15,
        };

        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2026, 4, 15));
        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2026, 5, 15));
    }

    [Fact]
    public void Nine_occurrences_fall_past_due_over_four_months()
    {
        // The exact case on the reference bills screen: a twice-monthly deposit due 15 April,
        // read on 23 August, showing nine occurrences past due.
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.TwiceAMonth,
            StartDate = new DateOnly(2026, 4, 15),
            SecondDayOfMonth = 30,
        };

        RecurrenceCalculator
            .CountBetween(rule, new DateOnly(2026, 4, 15), new DateOnly(2026, 8, 23))
            .ShouldBe(9);
    }
}

public sealed class WeekendShiftTests
{
    [Theory]
    [InlineData("2026-03-07", WeekendShift.PreviousBusinessDay, "2026-03-06")]
    [InlineData("2026-03-08", WeekendShift.PreviousBusinessDay, "2026-03-06")]
    [InlineData("2026-03-07", WeekendShift.NextBusinessDay, "2026-03-09")]
    [InlineData("2026-03-08", WeekendShift.NextBusinessDay, "2026-03-09")]
    [InlineData("2026-03-06", WeekendShift.NextBusinessDay, "2026-03-06")]
    [InlineData("2026-03-07", WeekendShift.None, "2026-03-07")]
    public void A_weekend_date_moves_according_to_the_policy(
        string date,
        WeekendShift shift,
        string expected) =>
        RecurrenceCalculator.ApplyWeekendShift(TestDates.Parse(date), shift).ShouldBe(TestDates.Parse(expected));

    [Fact]
    public void A_shift_never_becomes_the_anchor_for_the_next_occurrence()
    {
        // If the shifted date seeded the following one, a monthly bill landing on weekends
        // would walk backwards a day or two at a time until the series had drifted a week.
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = new DateOnly(2026, 8, 1),
            WeekendShift = WeekendShift.PreviousBusinessDay,
        };

        // 1 August 2026 is a Saturday, so it shifts to the 31st of July.
        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2026, 7, 31));

        // The next is still measured from 1 August: 1 September is a Tuesday.
        RecurrenceCalculator.OccurrenceAt(rule, 1).ShouldBe(new DateOnly(2026, 9, 1));
        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void A_shifted_series_still_returns_every_occurrence_in_a_window()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Weekly,
            StartDate = new DateOnly(2026, 3, 7),
            WeekendShift = WeekendShift.NextBusinessDay,
        };

        // Every anchor is a Saturday, so every date lands on the following Monday.
        List<DateOnly> dates =
        [
            .. RecurrenceCalculator.Occurrences(rule, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))
        ];

        dates.ShouldBe(
        [
            new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 16),
            new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 30),
        ]);
    }
}

public sealed class RecurrenceEndTests
{
    [Fact]
    public void A_series_with_no_end_keeps_going()
    {
        RecurrenceRule rule = RecurrenceRule.Monthly(new DateOnly(2026, 1, 1));

        RecurrenceCalculator.OccurrenceAt(rule, 500).ShouldNotBeNull();
    }

    [Fact]
    public void An_end_date_stops_the_series()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = new DateOnly(2026, 1, 1),
            EndKind = RecurrenceEndKind.OnDate,
            EndDate = new DateOnly(2026, 3, 31),
        };

        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 3, 1));
        RecurrenceCalculator.OccurrenceAt(rule, 3).ShouldBeNull();
    }

    [Fact]
    public void A_count_stops_the_series()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = new DateOnly(2026, 1, 1),
            EndKind = RecurrenceEndKind.AfterOccurrences,
            OccurrenceCount = 3,
        };

        RecurrenceCalculator.OccurrenceAt(rule, 2).ShouldBe(new DateOnly(2026, 3, 1));
        RecurrenceCalculator.OccurrenceAt(rule, 3).ShouldBeNull();

        RecurrenceCalculator
            .Occurrences(rule, null, new DateOnly(2030, 1, 1))
            .Count()
            .ShouldBe(3);
    }

    [Fact]
    public void The_last_occurrence_is_kept_even_when_a_shift_pushes_it_past_the_end()
    {
        // The end date bounds the pattern, not the adjusted date. Dropping the final bill
        // because it happened to land on a Saturday would lose a real payment.
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = new DateOnly(2026, 8, 1),
            WeekendShift = WeekendShift.NextBusinessDay,
            EndKind = RecurrenceEndKind.OnDate,
            EndDate = new DateOnly(2026, 8, 1),
        };

        RecurrenceCalculator.OccurrenceAt(rule, 0).ShouldBe(new DateOnly(2026, 8, 3));
    }
}

public sealed class NextDueTests
{
    [Fact]
    public void The_next_due_date_is_the_first_on_or_after_today()
    {
        RecurrenceRule rule = RecurrenceRule.Monthly(new DateOnly(2026, 1, 15));

        RecurrenceCalculator
            .NextOnOrAfter(rule, new DateOnly(2026, 3, 15))
            .ShouldBe(new DateOnly(2026, 3, 15));

        RecurrenceCalculator
            .NextOnOrAfter(rule, new DateOnly(2026, 3, 16))
            .ShouldBe(new DateOnly(2026, 4, 15));
    }

    [Fact]
    public void A_finished_series_has_no_next_date()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = new DateOnly(2026, 1, 1),
            EndKind = RecurrenceEndKind.AfterOccurrences,
            OccurrenceCount = 2,
        };

        RecurrenceCalculator.NextAfter(rule, new DateOnly(2026, 3, 1)).ShouldBeNull();
    }

    [Fact]
    public void A_window_before_the_series_starts_is_empty()
    {
        RecurrenceRule rule = RecurrenceRule.Monthly(new DateOnly(2026, 6, 1));

        RecurrenceCalculator
            .Occurrences(rule, new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 31))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Counting_a_window_includes_both_ends()
    {
        RecurrenceRule rule = RecurrenceRule.Monthly(new DateOnly(2026, 1, 15));

        RecurrenceCalculator
            .CountBetween(rule, new DateOnly(2026, 1, 15), new DateOnly(2026, 4, 15))
            .ShouldBe(4);
    }
}

public sealed class RecurrenceDescriberTests
{
    [Theory]
    [InlineData(RecurrenceFrequency.Once, 1, "Only once")]
    [InlineData(RecurrenceFrequency.Weekly, 1, "Weekly")]
    [InlineData(RecurrenceFrequency.EveryTwoWeeks, 1, "Every two weeks")]
    [InlineData(RecurrenceFrequency.TwiceAMonth, 1, "Twice a month")]
    [InlineData(RecurrenceFrequency.Monthly, 1, "Monthly")]
    [InlineData(RecurrenceFrequency.Quarterly, 1, "Every three months")]
    [InlineData(RecurrenceFrequency.Yearly, 1, "Yearly")]
    public void Frequencies_read_the_way_the_reference_books_word_them(
        RecurrenceFrequency frequency,
        int interval,
        string expected) =>
        RecurrenceDescriber.Describe(frequency, interval).ShouldBe(expected);

    [Fact]
    public void An_interval_is_spelled_out()
    {
        RecurrenceDescriber.Describe(RecurrenceFrequency.Monthly, 3).ShouldBe("Every 3 months");
        RecurrenceDescriber.Describe(RecurrenceFrequency.Weekly, 2).ShouldBe("Every 2 weeks");
    }

    [Fact]
    public void The_end_condition_is_spelled_out()
    {
        RecurrenceDescriber.DescribeEnd(RecurrenceRule.Monthly(new DateOnly(2026, 1, 1)))
            .ShouldBe("with no end date");

        RecurrenceDescriber.DescribeEnd(new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = new DateOnly(2026, 1, 1),
            EndKind = RecurrenceEndKind.AfterOccurrences,
            OccurrenceCount = 1,
        }).ShouldBe("for 1 occurrence");
    }
}

/// <summary>Shared date helper for the scheduling tests.</summary>
internal static class TestDates
{
    public static DateOnly Parse(string text) =>
        DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
