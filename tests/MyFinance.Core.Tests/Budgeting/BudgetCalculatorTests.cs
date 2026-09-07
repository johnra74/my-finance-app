using MyFinance.Core.Budgeting;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Budgeting;

public sealed class BudgetCalculatorTests
{
    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public void A_budget_reports_what_is_left()
    {
        BudgetPeriodResult period = BudgetCalculator.ForPeriod(
            [Plan(1, "Food : Groceries", 400m, March)],
            [Actual(1, -250m, March)],
            March);

        BudgetLineResult line = period.Lines.Single();

        // Everything here is positive: a budget is a statement of intent, and negating it to
        // match the register would make every comparison read backwards.
        line.Budgeted.ShouldBe(Money.FromDecimal(400m));
        line.Actual.ShouldBe(Money.FromDecimal(250m));
        line.Remaining.ShouldBe(Money.FromDecimal(150m));
        line.IsOverBudget.ShouldBeFalse();
        line.Progress.ShouldBe(0.625m);

        // The rounding is what matters; how a percent sign is spaced is the culture's affair.
        line.ProgressText.ShouldStartWith("63");
    }

    [Fact]
    public void Going_over_is_reported_as_a_negative_remainder()
    {
        BudgetLineResult line = BudgetCalculator.ForPeriod(
            [Plan(1, "Food : Groceries", 400m, March)],
            [Actual(1, -520m, March)],
            March).Lines.Single();

        line.IsOverBudget.ShouldBeTrue();
        line.Remaining.ShouldBe(Money.FromDecimal(-120m));
        line.Progress.ShouldBe(1.3m);

        // The bar must not overflow its track, but the overrun is still reported.
        line.ProgressCapped.ShouldBe(1m);
        line.Overrun.ShouldBe(0.3m);
    }

    [Fact]
    public void A_category_with_nothing_spent_is_untouched()
    {
        BudgetLineResult line = BudgetCalculator.ForPeriod(
            [Plan(1, "Leisure : Travel", 200m, March)],
            [],
            March).Lines.Single();

        line.Actual.ShouldBe(Money.Zero);
        line.Remaining.ShouldBe(Money.FromDecimal(200m));
        line.Progress.ShouldBe(0m);
    }

    [Fact]
    public void An_unspent_remainder_carries_forward_when_asked()
    {
        IReadOnlyList<BudgetPeriodResult> series = BudgetCalculator.Series(
            [
                Plan(1, "Leisure : Travel", 100m, new DateOnly(2026, 1, 1), rollsOver: true),
                Plan(1, "Leisure : Travel", 100m, new DateOnly(2026, 2, 1), rollsOver: true),
                Plan(1, "Leisure : Travel", 100m, new DateOnly(2026, 3, 1), rollsOver: true),
            ],
            [Actual(1, -40m, new DateOnly(2026, 2, 1))],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 1));

        // January spends nothing, so all of it travels.
        series[0].Lines[0].RolloverIn.ShouldBe(Money.Zero);
        series[0].Lines[0].RolloverOut.ShouldBe(Money.FromDecimal(100m));

        // February has 200 available and spends 40.
        series[1].Lines[0].RolloverIn.ShouldBe(Money.FromDecimal(100m));
        series[1].Lines[0].Available.ShouldBe(Money.FromDecimal(200m));
        series[1].Lines[0].Remaining.ShouldBe(Money.FromDecimal(160m));

        // March receives the accumulated remainder.
        series[2].Lines[0].RolloverIn.ShouldBe(Money.FromDecimal(160m));
        series[2].Lines[0].Available.ShouldBe(Money.FromDecimal(260m));
    }

    [Fact]
    public void Without_rollover_each_period_starts_afresh()
    {
        IReadOnlyList<BudgetPeriodResult> series = BudgetCalculator.Series(
            [
                Plan(1, "Food : Coffee", 50m, new DateOnly(2026, 1, 1)),
                Plan(1, "Food : Coffee", 50m, new DateOnly(2026, 2, 1)),
            ],
            [],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1));

        series[1].Lines[0].RolloverIn.ShouldBe(Money.Zero);
        series[1].Lines[0].Available.ShouldBe(Money.FromDecimal(50m));
    }

    [Fact]
    public void An_overspend_does_not_become_a_debt_against_the_next_month()
    {
        IReadOnlyList<BudgetPeriodResult> series = BudgetCalculator.Series(
            [
                Plan(1, "Food : Coffee", 50m, new DateOnly(2026, 1, 1), rollsOver: true),
                Plan(1, "Food : Coffee", 50m, new DateOnly(2026, 2, 1), rollsOver: true),
            ],
            [Actual(1, -90m, new DateOnly(2026, 1, 1))],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1));

        // The budget for February is what was set for February. Only an unspent remainder
        // travels; an overrun stays in the month it happened.
        series[0].Lines[0].Remaining.ShouldBe(Money.FromDecimal(-40m));
        series[1].Lines[0].RolloverIn.ShouldBe(Money.Zero);
        series[1].Lines[0].Available.ShouldBe(Money.FromDecimal(50m));
    }

    [Fact]
    public void The_rollover_chain_is_complete_even_when_the_window_starts_late()
    {
        // Asking only for March still has to walk January and February, because what is
        // carried in is the accumulated remainder of everything before it.
        IReadOnlyList<BudgetPeriodResult> series = BudgetCalculator.Series(
            [
                Plan(1, "Leisure : Travel", 100m, new DateOnly(2026, 1, 1), rollsOver: true),
                Plan(1, "Leisure : Travel", 100m, new DateOnly(2026, 2, 1), rollsOver: true),
                Plan(1, "Leisure : Travel", 100m, new DateOnly(2026, 3, 1), rollsOver: true),
            ],
            [],
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 1));

        series.Count.ShouldBe(1);
        series[0].Lines[0].RolloverIn.ShouldBe(Money.FromDecimal(200m));
    }

    [Fact]
    public void A_period_totals_its_spending_and_its_income_separately()
    {
        BudgetPeriodResult period = BudgetCalculator.ForPeriod(
            [
                Plan(1, "Food : Groceries", 400m, March),
                Plan(2, "Bills : Rent", 1_200m, March),
                Plan(3, "Income : Salary", 5_000m, March, kind: CategoryKind.Income),
            ],
            [
                Actual(1, -250m, March),
                Actual(2, -1_200m, March),
                Actual(3, 5_000m, March),
            ],
            March);

        period.BudgetedSpending.ShouldBe(Money.FromDecimal(1_600m));
        period.ActualSpending.ShouldBe(Money.FromDecimal(1_450m));
        period.BudgetedIncome.ShouldBe(Money.FromDecimal(5_000m));
        period.ActualIncome.ShouldBe(Money.FromDecimal(5_000m));

        period.PlannedNet.ShouldBe(Money.FromDecimal(3_400m));
        period.ActualNet.ShouldBe(Money.FromDecimal(3_550m));
        period.RemainingToSpend.ShouldBe(Money.FromDecimal(150m));
        period.OverBudgetCount.ShouldBe(0);
    }

    [Fact]
    public void The_over_budget_count_only_counts_spending_categories()
    {
        BudgetPeriodResult period = BudgetCalculator.ForPeriod(
            [
                Plan(1, "Food : Groceries", 400m, March),
                Plan(2, "Income : Salary", 5_000m, March, kind: CategoryKind.Income),
            ],
            [
                Actual(1, -520m, March),

                // Earning less than planned is not an overspend.
                Actual(2, 3_000m, March),
            ],
            March);

        period.OverBudgetCount.ShouldBe(1);
    }

    [Fact]
    public void Lines_come_back_in_category_order()
    {
        BudgetPeriodResult period = BudgetCalculator.ForPeriod(
            [
                Plan(1, "Transport : Fuel", 100m, March),
                Plan(2, "Bills : Rent", 100m, March),
                Plan(3, "Food : Coffee", 100m, March),
            ],
            [],
            March);

        period.Lines.Select(l => l.CategoryPath)
            .ShouldBe(["Bills : Rent", "Food : Coffee", "Transport : Fuel"]);
    }

    [Fact]
    public void A_year_rolls_twelve_months_into_one_line_per_category()
    {
        List<BudgetAllocation> plans =
        [
            .. Enumerable.Range(1, 12).Select(month =>
                Plan(1, "Food : Groceries", 400m, new DateOnly(2026, month, 1), rollsOver: true)),
        ];

        BudgetPeriodResult year = BudgetCalculator.ForYear(
            plans,
            [Actual(1, -250m, new DateOnly(2026, 1, 1)), Actual(1, -300m, new DateOnly(2026, 2, 1))],
            2026);

        BudgetLineResult line = year.Lines.Single();

        line.Budgeted.ShouldBe(Money.FromDecimal(4_800m));
        line.Actual.ShouldBe(Money.FromDecimal(550m));

        // Rollover is deliberately ignored over a whole year: what one month carried forward
        // another received, so counting it would show the same money twice.
        line.RolloverIn.ShouldBe(Money.Zero);
    }

    [Fact]
    public void A_book_with_no_budget_produces_nothing_rather_than_throwing()
    {
        BudgetCalculator.Series([], [], March, March).ShouldBeEmpty();
        BudgetCalculator.ForPeriod([], [], March).IsEmpty.ShouldBeTrue();
        BudgetCalculator.ForYear([], [], 2026).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_zero_budget_that_has_been_spent_against_reads_as_fully_used()
    {
        BudgetLineResult line = BudgetCalculator.ForPeriod(
            [Plan(1, "Food : Coffee", 0m, March)],
            [Actual(1, -20m, March)],
            March).Lines.Single();

        // Dividing by zero would be undefined; anything spent against nothing budgeted is
        // wholly over it.
        line.Progress.ShouldBe(1m);
        line.IsOverBudget.ShouldBeTrue();
    }

    private static BudgetAllocation Plan(
        int categoryId,
        string path,
        decimal amount,
        DateOnly period,
        bool rollsOver = false,
        CategoryKind kind = CategoryKind.Expense) =>
        new(categoryId, path, kind, period, Money.FromDecimal(amount), rollsOver);

    private static BudgetActual Actual(int categoryId, decimal amount, DateOnly period) =>
        new(categoryId, period, Money.FromDecimal(amount));
}
