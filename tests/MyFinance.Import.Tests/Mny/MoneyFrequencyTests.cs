using MyFinance.Core.Enums;
using MyFinance.Import.Mny;

namespace MyFinance.Import.Tests.Mny;

/// <summary>
/// Money's recurrence codes, and the refusal to guess at the ones nobody has verified.
/// </summary>
/// <remarks>
/// Every mapped pair below was established against a real book three ways: Money's own
/// on-screen label for one series of each kind, the observed spacing of the instances Money
/// actually generated, and mutual exclusivity across all 145 datable series in the file. The
/// working is in `specs/014-scheduled-bill-migration/plan.md`.
/// </remarks>
public class MoneyFrequencyTests
{
    [Fact]
    public void Monthly_is_frq_three_with_one_per_period()
    {
        // 118 series in the reference book; Money labels one of them "Monthly" on screen and
        // its generated instances sit 30 to 31 days apart.
        MoneyFrequency.Mapping mapped = MoneyFrequency.Map(3, 1);

        mapped.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
        mapped.Interval.ShouldBe(1);
        mapped.IsKnown.ShouldBeTrue();
    }

    [Fact]
    public void Twice_a_month_is_frq_three_with_two_per_period()
    {
        // 24 series; Money labels one "Twice a month" and its 226 instances sit 15 days apart.
        MoneyFrequency.Mapping mapped = MoneyFrequency.Map(3, 2);

        mapped.Frequency.ShouldBe(RecurrenceFrequency.TwiceAMonth);
        mapped.IsKnown.ShouldBeTrue();
    }

    [Fact]
    public void Quarterly_is_frq_four()
    {
        // 3 series; Money labels one "Every three months" and its instances sit 92 days apart.
        MoneyFrequency.Map(4, 1).Frequency.ShouldBe(RecurrenceFrequency.Quarterly);
    }

    [Fact]
    public void A_count_per_period_is_never_carried_through_as_an_interval()
    {
        // The trap this whole mapping exists to avoid. Money's cFrqInst=2 means *twice a
        // month*; our Interval=2 with Monthly means *every two months*. Passing one through as
        // the other halves a salary, silently, on the largest inflow in the book.
        MoneyFrequency.Mapping twiceMonthly = MoneyFrequency.Map(3, 2);

        twiceMonthly.Frequency.ShouldBe(RecurrenceFrequency.TwiceAMonth);
        twiceMonthly.Interval.ShouldBe(1);
        twiceMonthly.Interval.ShouldNotBe(2);
    }

    [Fact]
    public void An_absent_count_is_read_as_one_per_period_rather_than_as_unknown()
    {
        // A missing count is the ordinary case, not a mystery.
        MoneyFrequency.Map(3, null).Frequency.ShouldBe(RecurrenceFrequency.Monthly);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 1)]
    [InlineData(9, 1)]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 2)]
    public void A_code_with_no_verified_meaning_maps_to_unknown(int frq, double count)
    {
        // Eight of the eleven frequencies have never been seen in a real file. Guessing at one
        // produces a wrong due date, which 009 FR-024 judged worse than no due date at all.
        MoneyFrequency.Mapping mapped = MoneyFrequency.Map(frq, count);

        mapped.IsKnown.ShouldBeFalse();
        mapped.Frequency.ShouldBeNull();
    }

    [Fact]
    public void A_missing_code_maps_to_unknown()
    {
        MoneyFrequency.Map(null, null).IsKnown.ShouldBeFalse();
    }

    [Fact]
    public void Every_mapped_code_maps_to_exactly_one_frequency()
    {
        // No pair may be ambiguous: the same (frq, count) must always mean the same thing, or
        // the migration is guessing under a different name.
        var seen = new Dictionary<(int, int), RecurrenceFrequency>();

        for (int frq = 0; frq <= 12; frq++)
        {
            for (int count = 1; count <= 4; count++)
            {
                MoneyFrequency.Mapping mapped = MoneyFrequency.Map(frq, count);

                if (mapped.Frequency is RecurrenceFrequency f)
                {
                    seen.ShouldNotContainKey((frq, count));
                    seen[(frq, count)] = f;
                }
            }
        }

        seen.Count.ShouldBe(3, "three frequencies have been verified against a real book");
        seen.Values.ShouldBe(
            [RecurrenceFrequency.Monthly, RecurrenceFrequency.TwiceAMonth, RecurrenceFrequency.Quarterly],
            ignoreOrder: true);
    }

    [Fact]
    public void A_fractional_count_rounds_rather_than_being_refused()
    {
        // The column is stored as a floating-point value, so 2.0 may not arrive exactly.
        MoneyFrequency.Map(3, 1.9999).Frequency.ShouldBe(RecurrenceFrequency.TwiceAMonth);
        MoneyFrequency.Map(3, 1.0001).Frequency.ShouldBe(RecurrenceFrequency.Monthly);
    }
}
