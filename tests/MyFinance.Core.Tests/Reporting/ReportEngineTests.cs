using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Reporting;

namespace MyFinance.Core.Tests.Reporting;

public sealed class SpendingByCategoryTests
{
    [Fact]
    public void Outgoings_group_by_category_largest_first()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-120m, category: "Food : Groceries"),
            Entry(-30m, category: "Food : Coffee"),
            Entry(-80m, category: "Food : Groceries"),
            Entry(-200m, category: "Bills : Rent"),
        ],
        ReportFilter.Everything);

        report.Rows.Select(r => r.Label).ShouldBe(["Bills : Rent", "Food : Groceries", "Food : Coffee"]);
        report.Rows[0].Amount.ShouldBe(Money.FromDecimal(-200m));
        report.Rows[1].Amount.ShouldBe(Money.FromDecimal(-200m));
        report.Total.ShouldBe(Money.FromDecimal(-430m));
    }

    [Fact]
    public void Income_is_left_out_of_a_spending_report()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-100m, category: "Food : Groceries"),
            Entry(3_000m, category: "Income : Salary"),
        ],
        ReportFilter.Everything);

        report.Rows.Count.ShouldBe(1);
        report.Total.ShouldBe(Money.FromDecimal(-100m));
    }

    [Fact]
    public void Spending_on_a_credit_card_counts_the_same_as_spending_from_a_current_account()
    {
        // Grouped on the sign of the allocation, not on the kind of account it sits in.
        // Fifty pounds is fifty pounds however it was paid.
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-50m, category: "Food : Coffee", group: AccountGroup.Bank),
            Entry(-50m, category: "Food : Coffee", group: AccountGroup.Credit),
        ],
        ReportFilter.Everything);

        report.Rows.Single().Amount.ShouldBe(Money.FromDecimal(-100m));
    }

    [Fact]
    public void A_credit_card_payment_is_a_transfer_and_never_appears()
    {
        // The single most important exclusion in the whole engine. Paying a card from
        // chequing is not spending; counting it would double every card purchase.
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-500m, category: null, isTransfer: true, group: AccountGroup.Bank),
            Entry(500m, category: null, isTransfer: true, group: AccountGroup.Credit),
            Entry(-40m, category: "Food : Coffee", group: AccountGroup.Credit),
        ],
        ReportFilter.Everything);

        report.Rows.Single().Label.ShouldBe("Food : Coffee");
        report.Total.ShouldBe(Money.FromDecimal(-40m));
    }

    [Fact]
    public void Transfers_can_be_asked_for_explicitly()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
            [Entry(-500m, category: null, isTransfer: true)],
            ReportFilter.Everything with { IncludeTransfers = true });

        report.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public void Void_rows_contribute_nothing()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-100m, category: "Food : Coffee"),
            Entry(-999m, category: "Food : Coffee", isVoid: true),
        ],
        ReportFilter.Everything);

        report.Total.ShouldBe(Money.FromDecimal(-100m));
    }

    [Fact]
    public void Shares_are_positive_and_add_up_to_one()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-750m, category: "Bills : Rent"),
            Entry(-250m, category: "Food : Groceries"),
        ],
        ReportFilter.Everything);

        // Computed against the magnitude, so a report where every figure is negative still
        // yields readable percentages.
        report.Rows[0].Share.ShouldBe(0.75m);
        report.Rows[1].Share.ShouldBe(0.25m);
        report.Rows.Sum(r => r.Share).ShouldBe(1m);
    }

    [Fact]
    public void Uncategorized_spending_is_surfaced_rather_than_hidden_in_the_percentages()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-100m, category: "Food : Coffee"),
            Entry(-400m, category: null),
        ],
        ReportFilter.Everything);

        report.HasUncategorized.ShouldBeTrue();
        report.UncategorizedCount.ShouldBe(1);
        report.UncategorizedAmount.ShouldBe(Money.FromDecimal(-400m));
        report.UncategorizedWarning.ShouldContain("no category assigned");
    }

    [Fact]
    public void Uncategorized_rows_can_be_excluded_altogether()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
            [Entry(-100m, category: "Food : Coffee"), Entry(-400m, category: null)],
            ReportFilter.Everything with { IncludeUncategorized = false });

        report.Rows.Count.ShouldBe(1);
        report.Total.ShouldBe(Money.FromDecimal(-100m));
    }

    [Fact]
    public void Subcategories_can_be_rolled_up_into_their_heading()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-100m, category: "Food : Coffee", parent: "Food"),
            Entry(-200m, category: "Food : Groceries", parent: "Food"),
            Entry(-300m, category: "Bills : Rent", parent: "Bills"),
        ],
        ReportFilter.Everything with { RollUpToParent = true });

        report.Rows.Count.ShouldBe(2);
        report.Rows.Single(r => r.Label == "Food").Amount.ShouldBe(Money.FromDecimal(-300m));
    }

    [Fact]
    public void A_date_range_bounds_the_report_at_both_ends()
    {
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-100m, category: "Food : Coffee", date: new DateOnly(2025, 12, 31)),
            Entry(-200m, category: "Food : Coffee", date: new DateOnly(2026, 1, 1)),
            Entry(-300m, category: "Food : Coffee", date: new DateOnly(2026, 12, 31)),
            Entry(-400m, category: "Food : Coffee", date: new DateOnly(2027, 1, 1)),
        ],
        ReportFilter.Everything with
        {
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 12, 31),
        });

        report.Total.ShouldBe(Money.FromDecimal(-500m));
    }

    [Fact]
    public void A_split_transaction_is_counted_once_per_category_but_once_overall()
    {
        // Two allocations of one transaction: both categories get their share, and the
        // transaction count stays at one.
        GroupedReport report = ReportEngine.SpendingByCategory(
        [
            Entry(-70m, category: "Food : Groceries", transactionId: 1),
            Entry(-30m, category: "Home : Cleaning", transactionId: 1),
        ],
        ReportFilter.Everything);

        report.Rows.Count.ShouldBe(2);
        report.EntryCount.ShouldBe(1);
        report.Total.ShouldBe(Money.FromDecimal(-100m));
    }

    [Fact]
    public void An_empty_result_is_reported_as_empty_rather_than_throwing()
    {
        GroupedReport report = ReportEngine.SpendingByCategory([], ReportFilter.Everything);

        report.IsEmpty.ShouldBeTrue();
        report.Total.ShouldBe(Money.Zero);
        report.UncategorizedWarning.ShouldBe(string.Empty);
    }

    internal static ReportEntry Entry(
        decimal amount,
        string? category = null,
        string? parent = null,
        string? payee = null,
        DateOnly? date = null,
        bool isTransfer = false,
        bool isVoid = false,
        AccountGroup group = AccountGroup.Bank,
        int accountId = 1,
        int transactionId = 0) =>
        new()
        {
            TransactionId = transactionId == 0 ? Interlocked.Increment(ref _nextId) : transactionId,
            AccountId = accountId,
            AccountName = "Account",
            AccountGroup = group,
            Date = date ?? new DateOnly(2026, 6, 15),
            PayeeId = payee is null ? null : payee.GetHashCode(StringComparison.Ordinal),
            PayeeName = payee,
            CategoryId = category is null ? null : category.GetHashCode(StringComparison.Ordinal),
            CategoryPath = category,
            CategoryParent = parent ?? category?.Split(" : ")[0],
            CategoryKind = amount < 0 ? Enums.CategoryKind.Expense : Enums.CategoryKind.Income,
            Amount = Money.FromDecimal(amount),
            IsTransfer = isTransfer,
            IsVoid = isVoid,
        };

    private static int _nextId = 1000;
}

public sealed class SpendingByPayeeTests
{
    [Fact]
    public void Outgoings_group_by_who_was_paid()
    {
        GroupedReport report = ReportEngine.SpendingByPayee(
        [
            SpendingByCategoryTests.Entry(-40m, payee: "Shell"),
            SpendingByCategoryTests.Entry(-35m, payee: "Shell"),
            SpendingByCategoryTests.Entry(-12m, payee: "Blue Bottle"),
        ],
        ReportFilter.Everything);

        report.Rows[0].Label.ShouldBe("Shell");
        report.Rows[0].Amount.ShouldBe(Money.FromDecimal(-75m));
        report.Rows[1].Label.ShouldBe("Blue Bottle");
    }

    [Fact]
    public void Transactions_with_no_payee_are_grouped_under_a_label_of_their_own()
    {
        GroupedReport report = ReportEngine.SpendingByPayee(
            [SpendingByCategoryTests.Entry(-60m)],
            ReportFilter.Everything);

        report.Rows.Single().Label.ShouldBe(ReportEngine.NoPayeeLabel);
    }
}

public sealed class TimeSeriesTests
{
    [Fact]
    public void Income_and_spending_are_reported_separately_per_month()
    {
        TimeSeriesReport report = ReportEngine.IncomeAndSpendingOverTime(
        [
            SpendingByCategoryTests.Entry(3_000m, date: new DateOnly(2026, 1, 15)),
            SpendingByCategoryTests.Entry(-1_200m, date: new DateOnly(2026, 1, 5)),
            SpendingByCategoryTests.Entry(3_000m, date: new DateOnly(2026, 2, 15)),
        ],
        ReportFilter.Everything);

        report.Periods.Count.ShouldBe(2);
        report.Periods[0].Label.ShouldBe("Jan 2026");
        report.Periods[0].Income.ShouldBe(Money.FromDecimal(3_000m));
        report.Periods[0].Spending.ShouldBe(Money.FromDecimal(-1_200m));
        report.Periods[0].Net.ShouldBe(Money.FromDecimal(1_800m));

        report.TotalIncome.ShouldBe(Money.FromDecimal(6_000m));
        report.Net.ShouldBe(Money.FromDecimal(4_800m));
    }

    [Fact]
    public void A_month_with_nothing_in_it_is_still_a_month()
    {
        TimeSeriesReport report = ReportEngine.IncomeAndSpendingOverTime(
        [
            SpendingByCategoryTests.Entry(-100m, date: new DateOnly(2026, 1, 15)),
            SpendingByCategoryTests.Entry(-100m, date: new DateOnly(2026, 4, 15)),
        ],
        ReportFilter.Everything);

        // Leaving the gap out would make an axis lie about the passage of time and a trend
        // line join across months that are really there.
        report.Periods.Count.ShouldBe(4);
        report.Periods.Select(p => p.Label).ShouldBe(["Jan 2026", "Feb 2026", "Mar 2026", "Apr 2026"]);
        report.Periods[1].Income.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Quarters_and_years_group_as_they_should()
    {
        List<ReportEntry> entries =
        [
            SpendingByCategoryTests.Entry(-100m, date: new DateOnly(2026, 1, 15)),
            SpendingByCategoryTests.Entry(-100m, date: new DateOnly(2026, 3, 15)),
            SpendingByCategoryTests.Entry(-100m, date: new DateOnly(2026, 7, 15)),
        ];

        TimeSeriesReport quarterly = ReportEngine.IncomeAndSpendingOverTime(
            entries, ReportFilter.Everything with { Period = ReportPeriod.Quarterly });

        quarterly.Periods[0].Label.ShouldBe("Q1 2026");
        quarterly.Periods[0].Spending.ShouldBe(Money.FromDecimal(-200m));

        TimeSeriesReport yearly = ReportEngine.IncomeAndSpendingOverTime(
            entries, ReportFilter.Everything with { Period = ReportPeriod.Yearly });

        yearly.Periods.Count.ShouldBe(1);
        yearly.Periods[0].Label.ShouldBe("2026");
    }

    [Fact]
    public void The_average_says_what_is_typically_left_over()
    {
        TimeSeriesReport report = ReportEngine.IncomeAndSpendingOverTime(
        [
            SpendingByCategoryTests.Entry(1_000m, date: new DateOnly(2026, 1, 15)),
            SpendingByCategoryTests.Entry(2_000m, date: new DateOnly(2026, 2, 15)),
        ],
        ReportFilter.Everything);

        report.AverageNet.ShouldBe(Money.FromDecimal(1_500m));
    }
}

public sealed class NetWorthTests
{
    [Fact]
    public void Debt_reduces_net_worth()
    {
        IReadOnlyList<NetWorthPoint> points = ReportEngine.NetWorthOverTime(
            new Dictionary<int, (AccountGroup, Money)>
            {
                [1] = (AccountGroup.Bank, Money.FromDecimal(10_000m)),
                [2] = (AccountGroup.Credit, Money.FromDecimal(-2_000m)),
            },
            [],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            ReportPeriod.Monthly);

        NetWorthPoint point = points.Single();

        point.Assets.ShouldBe(Money.FromDecimal(10_000m));
        point.Liabilities.ShouldBe(Money.FromDecimal(-2_000m));
        point.NetWorth.ShouldBe(Money.FromDecimal(8_000m));

        // Plotted as a positive bar even though it is held as a negative balance.
        point.LiabilitiesMagnitude.ShouldBe(Money.FromDecimal(2_000m));
    }

    [Fact]
    public void Every_movement_up_to_a_date_counts_not_only_those_in_the_window()
    {
        IReadOnlyList<NetWorthPoint> points = ReportEngine.NetWorthOverTime(
            new Dictionary<int, (AccountGroup, Money)>
            {
                [1] = (AccountGroup.Bank, Money.FromDecimal(1_000m)),
            },
            [
                SpendingByCategoryTests.Entry(-100m, date: new DateOnly(2025, 6, 1), accountId: 1),
                SpendingByCategoryTests.Entry(-200m, date: new DateOnly(2026, 1, 15), accountId: 1),
                SpendingByCategoryTests.Entry(-400m, date: new DateOnly(2026, 2, 15), accountId: 1),
            ],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 28),
            ReportPeriod.Monthly);

        // Net worth at a date is everything that ever happened up to it, so the 2025 payment
        // has to be in the January figure even though it falls outside the window.
        points[0].NetWorth.ShouldBe(Money.FromDecimal(700m));
        points[1].NetWorth.ShouldBe(Money.FromDecimal(300m));
    }
}

public sealed class ComparisonTests
{
    [Fact]
    public void Two_windows_are_compared_category_by_category()
    {
        List<ReportEntry> entries =
        [
            SpendingByCategoryTests.Entry(-100m, category: "Food : Coffee", date: new DateOnly(2026, 1, 15)),
            SpendingByCategoryTests.Entry(-150m, category: "Food : Coffee", date: new DateOnly(2026, 2, 15)),
            SpendingByCategoryTests.Entry(-500m, category: "Bills : Rent", date: new DateOnly(2026, 1, 5)),
            SpendingByCategoryTests.Entry(-500m, category: "Bills : Rent", date: new DateOnly(2026, 2, 5)),
        ];

        ComparisonReport report = ReportEngine.CompareByCategory(
            entries,
            ReportFilter.Everything with { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 31) },
            ReportFilter.Everything with { From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 2, 28) },
            "January",
            "February");

        // Ordered by how much moved, so what changed is at the top rather than whatever is
        // biggest in absolute terms.
        ComparisonRow coffee = report.Rows[0];
        coffee.Label.ShouldBe("Food : Coffee");
        coffee.First.ShouldBe(Money.FromDecimal(-100m));
        coffee.Second.ShouldBe(Money.FromDecimal(-150m));
        coffee.Change.ShouldBe(Money.FromDecimal(-50m));
        coffee.ChangeShare.ShouldBe(-0.5m);
        coffee.SpendingRose.ShouldBeTrue();

        report.Rows[1].Change.ShouldBe(Money.Zero);
    }

    [Fact]
    public void A_category_that_appeared_from_nothing_has_no_percentage()
    {
        ComparisonReport report = ReportEngine.CompareByCategory(
            [SpendingByCategoryTests.Entry(-80m, category: "Leisure : Travel", date: new DateOnly(2026, 2, 15))],
            ReportFilter.Everything with { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 31) },
            ReportFilter.Everything with { From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 2, 28) },
            "January",
            "February");

        // Infinity is not a useful thing to show a person.
        ComparisonRow row = report.Rows.Single();
        row.First.ShouldBe(Money.Zero);
        row.ChangeShare.ShouldBeNull();
        row.ChangeShareText.ShouldBe("—");
    }
}

public sealed class ReportFilterTests
{
    [Fact]
    public void An_account_filter_restricts_to_those_accounts()
    {
        ReportFilter filter = ReportFilter.Everything with { AccountIds = [1] };

        filter.Matches(SpendingByCategoryTests.Entry(-10m, accountId: 1)).ShouldBeTrue();
        filter.Matches(SpendingByCategoryTests.Entry(-10m, accountId: 2)).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_account_filter_means_all_of_them()
    {
        ReportFilter.Everything.Matches(SpendingByCategoryTests.Entry(-10m, accountId: 7)).ShouldBeTrue();
    }

    [Fact]
    public void A_category_filter_excludes_uncategorized_rows_by_construction()
    {
        // A row with no category cannot match a filter that names specific ones.
        ReportFilter filter = ReportFilter.Everything with { CategoryIds = [42] };

        filter.Matches(SpendingByCategoryTests.Entry(-10m, category: null)).ShouldBeFalse();
    }
}
