using MyFinance.Core.Primitives;
using MyFinance.Core.Scheduling;

namespace MyFinance.Core.Tests.Scheduling;

public sealed class CashFlowForecasterTests
{
    [Fact]
    public void An_empty_schedule_still_draws_the_opening_balance()
    {
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.FromDecimal(1_000m),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 12, 31),
            []);

        projection.Points.Count.ShouldBe(1);
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(1_000m));
        projection.LowestBalance.ShouldBe(Money.FromDecimal(1_000m));
        projection.GoesNegative.ShouldBeFalse();
    }

    [Fact]
    public void A_monthly_bill_walks_the_balance_down()
    {
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.FromDecimal(1_000m),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 11, 30),
            [Item(1, "Rent", -300m, RecurrenceRule.Monthly(new DateOnly(2026, 9, 5)))]);

        projection.Events.Count.ShouldBe(3);
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(100m));
        projection.TotalOut.ShouldBe(Money.FromDecimal(-900m));
        projection.TotalIn.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Income_and_outgoings_net_off_by_day()
    {
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.Zero,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            [
                Item(1, "Salary", 3_000m, RecurrenceRule.Monthly(new DateOnly(2026, 9, 15))),
                Item(2, "Rent", -1_200m, RecurrenceRule.Monthly(new DateOnly(2026, 9, 15))),
            ]);

        ForecastPoint day = projection.Points.Single(p => p.Date == new DateOnly(2026, 9, 15));

        day.Change.ShouldBe(Money.FromDecimal(1_800m));
        day.Events.Count.ShouldBe(2);

        // The bill is listed first, because on a day carrying both the money goes out before
        // it comes in. A forecast exists to find trouble, not to look reassuring.
        day.Events[0].Description.ShouldBe("Rent");
    }

    [Fact]
    public void The_lowest_point_is_found_even_when_the_close_looks_healthy()
    {
        // The number that actually matters. A large bill early followed by a salary ends the
        // month comfortably while going overdrawn in between.
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.FromDecimal(500m),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            [
                Item(1, "Mortgage", -2_000m, RecurrenceRule.Once(new DateOnly(2026, 9, 3))),
                Item(2, "Salary", 4_000m, RecurrenceRule.Once(new DateOnly(2026, 9, 25))),
            ]);

        projection.ClosingBalance.ShouldBe(Money.FromDecimal(2_500m));

        projection.GoesNegative.ShouldBeTrue();
        projection.LowestBalance.ShouldBe(Money.FromDecimal(-1_500m));
        projection.LowestBalanceDate.ShouldBe(new DateOnly(2026, 9, 3));
        projection.FirstNegativeDate.ShouldBe(new DateOnly(2026, 9, 3));
    }

    [Fact]
    public void A_projection_that_never_dips_reports_no_trouble()
    {
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.FromDecimal(5_000m),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            [Item(1, "Rent", -1_200m, RecurrenceRule.Once(new DateOnly(2026, 9, 5)))]);

        projection.GoesNegative.ShouldBeFalse();
        projection.FirstNegativeDate.ShouldBeNull();
    }

    [Fact]
    public void Estimated_amounts_are_flagged_through_to_the_projection()
    {
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.FromDecimal(1_000m),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            [Item(1, "Electricity", -120m, RecurrenceRule.Once(new DateOnly(2026, 9, 10)), isEstimate: true)]);

        projection.IncludesEstimates.ShouldBeTrue();
        projection.Events.Single().IsEstimate.ShouldBeTrue();
    }

    [Fact]
    public void A_twice_monthly_deposit_is_projected_twice_a_month()
    {
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.Zero,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 31),
            [
                Item(1, "Salary", 2_750m, new RecurrenceRule
                {
                    Frequency = Core.Enums.RecurrenceFrequency.TwiceAMonth,
                    StartDate = new DateOnly(2026, 9, 15),
                    SecondDayOfMonth = 30,
                }),
            ]);

        projection.Events.Count.ShouldBe(4);
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(11_000m));
    }

    [Fact]
    public void Nothing_before_the_window_is_projected_into_it()
    {
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.FromDecimal(1_000m),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            [Item(1, "Old bill", -100m, RecurrenceRule.Once(new DateOnly(2026, 3, 1)))]);

        // A past-due bill belongs on the bills list, not silently folded into a future
        // balance where it would look like a payment still to come.
        projection.Events.ShouldBeEmpty();
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(1_000m));
    }

    [Fact]
    public void A_series_that_ends_stops_contributing()
    {
        CashFlowProjection projection = CashFlowForecaster.Project(
            Money.Zero,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 12, 31),
            [
                Item(1, "Car loan", -400m, new RecurrenceRule
                {
                    Frequency = Core.Enums.RecurrenceFrequency.Monthly,
                    StartDate = new DateOnly(2026, 9, 10),
                    EndKind = Core.Enums.RecurrenceEndKind.AfterOccurrences,
                    OccurrenceCount = 2,
                }),
            ]);

        projection.Events.Count.ShouldBe(2);
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(-800m));
    }

    private static ForecastItem Item(
        int id,
        string description,
        decimal amount,
        RecurrenceRule rule,
        bool isEstimate = false) =>
        new(id, description, Money.FromDecimal(amount), rule, isEstimate);
}
