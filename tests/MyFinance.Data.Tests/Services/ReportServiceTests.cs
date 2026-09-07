using MyFinance.Core.Budgeting;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Reporting;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

public sealed class ReportServiceTests
{
    private static readonly ReportFilter Year2026 = ReportFilter.Everything with
    {
        From = new DateOnly(2026, 1, 1),
        To = new DateOnly(2026, 12, 31),
    };

    [Fact]
    public async Task Spending_groups_by_category_over_a_real_book()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 10_000m);

        int groceries = await book.CategoryIdAsync("Food : Groceries");
        int coffee = await book.CategoryIdAsync("Food : Coffee");
        int rent = await book.CategoryIdAsync("Bills : Rent");

        await book.AddTransactionAsync(account, -120m, new DateOnly(2026, 3, 2), categoryId: groceries);
        await book.AddTransactionAsync(account, -80m, new DateOnly(2026, 3, 9), categoryId: groceries);
        await book.AddTransactionAsync(account, -18m, new DateOnly(2026, 3, 3), categoryId: coffee);
        await book.AddTransactionAsync(account, -1_200m, new DateOnly(2026, 3, 1), categoryId: rent);

        ReportOutput output = await book.Reports.RunAsync(ReportKind.SpendingByCategory, Year2026);

        GroupedReport report = output.Grouped!;

        report.Rows[0].Label.ShouldBe("Bills : Rent");
        report.Rows[0].Amount.ShouldBe(Money.FromDecimal(-1_200m));
        report.Rows[1].Label.ShouldBe("Food : Groceries");
        report.Rows[1].Amount.ShouldBe(Money.FromDecimal(-200m));
        report.Total.ShouldBe(Money.FromDecimal(-1_418m));
    }

    [Fact]
    public async Task A_transfer_between_your_own_accounts_never_appears_as_spending()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 10_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.AddTransactionAsync(checking, -120m, new DateOnly(2026, 3, 2), categoryId: groceries);

        await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Date = new DateOnly(2026, 3, 5),
            Amount = Money.FromDecimal(-5_000m),
            TransferAccountId = savings,
        });

        ReportOutput output = await book.Reports.RunAsync(ReportKind.SpendingByCategory, Year2026);

        // Moving money to savings is not spending; counting it would make the report useless.
        output.Grouped!.Total.ShouldBe(Money.FromDecimal(-120m));
        output.Grouped.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Spending_on_a_credit_card_is_counted_alongside_spending_from_a_bank()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int card = await book.AddAccountAsync("Amex", AccountType.CreditCard);
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        await book.AddTransactionAsync(checking, -20m, new DateOnly(2026, 3, 2), categoryId: coffee);
        await book.AddTransactionAsync(card, -30m, new DateOnly(2026, 3, 3), categoryId: coffee);

        ReportOutput output = await book.Reports.RunAsync(ReportKind.SpendingByCategory, Year2026);

        output.Grouped!.Rows.Single().Amount.ShouldBe(Money.FromDecimal(-50m));
    }

    [Fact]
    public async Task A_split_transaction_reaches_both_of_its_categories()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int groceries = await book.CategoryIdAsync("Food : Groceries");
        int household = await book.CategoryIdAsync("Home : Household supplies");

        await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            Date = new DateOnly(2026, 3, 2),
            Amount = Money.FromDecimal(-142.83m),
            PayeeName = "Costco",
            Splits =
            [
                new SplitDraft { CategoryId = groceries, Amount = Money.FromDecimal(-118.20m) },
                new SplitDraft { CategoryId = household, Amount = Money.FromDecimal(-24.63m) },
            ],
        });

        GroupedReport report = (await book.Reports.RunAsync(ReportKind.SpendingByCategory, Year2026))
            .Grouped!;

        report.Rows.Count.ShouldBe(2);
        report.Total.ShouldBe(Money.FromDecimal(-142.83m));

        // Both categories are credited, and the transaction is still one transaction.
        report.EntryCount.ShouldBe(1);
    }

    [Fact]
    public async Task Uncategorized_spending_is_reported_above_the_chart()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        await book.AddTransactionAsync(account, -20m, new DateOnly(2026, 3, 2), categoryId: coffee);
        await book.AddTransactionAsync(account, -400m, new DateOnly(2026, 3, 3));

        ReportOutput output = await book.Reports.RunAsync(ReportKind.SpendingByCategory, Year2026);

        output.HasUncategorized.ShouldBeTrue();
        output.UncategorizedWarning.ShouldContain("no category assigned");
    }

    [Fact]
    public async Task Spending_groups_by_payee()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);

        await book.AddTransactionAsync(account, -40m, new DateOnly(2026, 3, 2), payee: "Shell");
        await book.AddTransactionAsync(account, -35m, new DateOnly(2026, 3, 9), payee: "Shell");
        await book.AddTransactionAsync(account, -12m, new DateOnly(2026, 3, 3), payee: "Blue Bottle");

        GroupedReport report = (await book.Reports.RunAsync(ReportKind.SpendingByPayee, Year2026))
            .Grouped!;

        report.Rows[0].Label.ShouldBe("Shell");
        report.Rows[0].Amount.ShouldBe(Money.FromDecimal(-75m));
        report.Rows[0].Count.ShouldBe(2);
    }

    [Fact]
    public async Task Income_and_spending_are_reported_month_by_month()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int salary = await book.CategoryIdAsync("Income : Salary");
        int rent = await book.CategoryIdAsync("Bills : Rent");

        await book.AddTransactionAsync(account, 3_000m, new DateOnly(2026, 1, 15), categoryId: salary);
        await book.AddTransactionAsync(account, -1_200m, new DateOnly(2026, 1, 1), categoryId: rent);
        await book.AddTransactionAsync(account, 3_000m, new DateOnly(2026, 2, 15), categoryId: salary);

        TimeSeriesReport report =
            (await book.Reports.RunAsync(ReportKind.IncomeAndSpendingOverTime, Year2026)).Series!;

        report.Periods.Count.ShouldBe(2);
        report.Periods[0].Income.ShouldBe(Money.FromDecimal(3_000m));
        report.Periods[0].Spending.ShouldBe(Money.FromDecimal(-1_200m));
        report.Net.ShouldBe(Money.FromDecimal(4_800m));
    }

    [Fact]
    public async Task Account_balances_are_grouped_and_signed_as_the_account_list_shows_them()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Everyday Checking", openingBalance: 1_000m);
        int card = await book.AddAccountAsync("Rewards Card", AccountType.CreditCard);

        await book.AddTransactionAsync(checking, -200m, new DateOnly(2026, 3, 2));
        await book.AddTransactionAsync(card, -410.25m, new DateOnly(2026, 3, 3));

        IReadOnlyList<AccountBalanceRow> rows =
            (await book.Reports.RunAsync(ReportKind.AccountBalances, ReportFilter.Everything)).Balances!;

        rows[0].Name.ShouldBe("Everyday Checking");
        rows[0].Balance.ShouldBe(Money.FromDecimal(800m));
        rows[1].Group.ShouldBe(AccountGroup.Credit);
        rows[1].Balance.ShouldBe(Money.FromDecimal(-410.25m));
    }

    [Fact]
    public async Task Net_worth_over_time_nets_debt_against_what_is_held()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 10_000m);
        int card = await book.AddAccountAsync("Amex", AccountType.CreditCard);

        await book.AddTransactionAsync(card, -2_000m, new DateOnly(2026, 1, 10));

        IReadOnlyList<NetWorthPoint> points = (await book.Reports.RunAsync(
            ReportKind.NetWorthOverTime,
            ReportFilter.Everything with
            {
                From = new DateOnly(2026, 1, 1),
                To = new DateOnly(2026, 2, 28),
            })).NetWorth!;

        points[0].Assets.ShouldBe(Money.FromDecimal(10_000m));
        points[0].Liabilities.ShouldBe(Money.FromDecimal(-2_000m));
        points[0].NetWorth.ShouldBe(Money.FromDecimal(8_000m));
    }

    [Fact]
    public async Task Drilling_into_a_category_lists_the_transactions_behind_it()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int coffee = await book.CategoryIdAsync("Food : Coffee");
        int rent = await book.CategoryIdAsync("Bills : Rent");

        await book.AddTransactionAsync(account, -4.50m, new DateOnly(2026, 3, 2), categoryId: coffee);
        await book.AddTransactionAsync(account, -5.25m, new DateOnly(2026, 3, 9), categoryId: coffee);
        await book.AddTransactionAsync(account, -1_200m, new DateOnly(2026, 3, 1), categoryId: rent);

        IReadOnlyList<ReportEntry> rows = await book.Reports.DrillDownAsync(Year2026, categoryId: coffee);

        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.CategoryPath == "Food : Coffee");
        rows[0].Date.ShouldBe(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public async Task Drilling_into_the_uncategorized_row_finds_what_needs_filing()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        await book.AddTransactionAsync(account, -4.50m, new DateOnly(2026, 3, 2), categoryId: coffee);
        await book.AddTransactionAsync(account, -400m, new DateOnly(2026, 3, 3));

        IReadOnlyList<ReportEntry> rows =
            await book.Reports.DrillDownAsync(Year2026, uncategorizedOnly: true);

        rows.Single().Amount.ShouldBe(Money.FromDecimal(-400m));
    }

    [Fact]
    public async Task A_comparison_measures_the_window_against_the_one_before_it()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 10_000m);
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        await book.AddTransactionAsync(account, -100m, new DateOnly(2026, 1, 15), categoryId: coffee);
        await book.AddTransactionAsync(account, -150m, new DateOnly(2026, 2, 15), categoryId: coffee);

        ComparisonReport report = (await book.Reports.RunAsync(
            ReportKind.SpendingComparison,
            ReportFilter.Everything with
            {
                From = new DateOnly(2026, 2, 1),
                To = new DateOnly(2026, 2, 28),
            })).Comparison!;

        ComparisonRow row = report.Rows.Single();
        row.First.ShouldBe(Money.FromDecimal(-100m));
        row.Second.ShouldBe(Money.FromDecimal(-150m));
    }

    [Fact]
    public async Task An_empty_book_reports_nothing_rather_than_throwing()
    {
        using var book = new BookHarness();

        ReportOutput output = await book.Reports.RunAsync(ReportKind.SpendingByCategory, Year2026);

        output.Grouped!.IsEmpty.ShouldBeTrue();
        output.Grouped.Total.ShouldBe(Money.Zero);
    }
}

public sealed class BudgetServiceTests
{
    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public async Task A_budget_is_measured_against_what_was_actually_spent()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries,
            PeriodStart = March,
            Amount = Money.FromDecimal(400m),
        });

        await book.AddTransactionAsync(account, -250m, new DateOnly(2026, 3, 10), categoryId: groceries);

        BudgetLineResult line = (await book.Budgets.GetMonthAsync(March)).Lines.Single();

        line.Budgeted.ShouldBe(Money.FromDecimal(400m));
        line.Actual.ShouldBe(Money.FromDecimal(250m));
        line.Remaining.ShouldBe(Money.FromDecimal(150m));
    }

    [Fact]
    public async Task A_budget_is_stored_positive_however_it_was_supplied()
    {
        using var book = new BookHarness();
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries,
            PeriodStart = March,

            // A caller thinking in register signs should still get a sensible budget.
            Amount = Money.FromDecimal(-400m),
        });

        (await book.Budgets.GetMonthAsync(March)).Lines.Single()
            .Budgeted.ShouldBe(Money.FromDecimal(400m));
    }

    [Fact]
    public async Task Setting_a_budget_twice_replaces_it_rather_than_adding_a_second()
    {
        using var book = new BookHarness();
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries, PeriodStart = March, Amount = Money.FromDecimal(400m),
        });

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries, PeriodStart = March, Amount = Money.FromDecimal(450m),
        });

        BudgetPeriodResult period = await book.Budgets.GetMonthAsync(March);

        period.Lines.Count.ShouldBe(1);
        period.Lines[0].Budgeted.ShouldBe(Money.FromDecimal(450m));
    }

    [Fact]
    public async Task Any_day_of_the_month_sets_the_budget_for_that_month()
    {
        using var book = new BookHarness();
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries,
            PeriodStart = new DateOnly(2026, 3, 17),
            Amount = Money.FromDecimal(400m),
        });

        (await book.Budgets.GetMonthAsync(March)).Lines.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_budget_and_a_spending_report_agree_about_the_same_month()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 10_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries, PeriodStart = March, Amount = Money.FromDecimal(400m),
        });

        await book.AddTransactionAsync(checking, -250m, new DateOnly(2026, 3, 10), categoryId: groceries);

        // A transfer and a void must be invisible to both, or the two screens would disagree
        // about what the month came to.
        await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Date = new DateOnly(2026, 3, 12),
            Amount = Money.FromDecimal(-1_000m),
            TransferAccountId = savings,
        });

        int voided = await book.AddTransactionAsync(
            checking, -99m, new DateOnly(2026, 3, 14), categoryId: groceries);
        await book.Register.SetVoidAsync(voided, isVoid: true);

        BudgetLineResult line = (await book.Budgets.GetMonthAsync(March)).Lines.Single();

        GroupedReport report = (await book.Reports.RunAsync(
            ReportKind.SpendingByCategory,
            ReportFilter.Everything with { From = March, To = new DateOnly(2026, 3, 31) })).Grouped!;

        line.Actual.ShouldBe(Money.FromDecimal(250m));
        report.Total.Abs().ShouldBe(line.Actual);
    }

    [Fact]
    public async Task A_month_can_be_copied_across_a_year()
    {
        using var book = new BookHarness();
        int groceries = await book.CategoryIdAsync("Food : Groceries");
        int rent = await book.CategoryIdAsync("Bills : Rent");

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries, PeriodStart = new DateOnly(2026, 1, 1), Amount = Money.FromDecimal(400m),
        });

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = rent, PeriodStart = new DateOnly(2026, 1, 1), Amount = Money.FromDecimal(1_200m),
        });

        int written = await book.Budgets.CopyMonthAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), months: 11);

        // Typing twelve of everything is how people give up on budgeting.
        written.ShouldBe(22);

        BudgetPeriodResult year = await book.Budgets.GetYearAsync(2026);
        year.Lines.Single(l => l.CategoryPath == "Food : Groceries")
            .Budgeted.ShouldBe(Money.FromDecimal(4_800m));
    }

    [Fact]
    public async Task Copying_leaves_an_existing_figure_alone_unless_told_otherwise()
    {
        using var book = new BookHarness();
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries, PeriodStart = new DateOnly(2026, 1, 1), Amount = Money.FromDecimal(400m),
        });

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries, PeriodStart = new DateOnly(2026, 2, 1), Amount = Money.FromDecimal(999m),
        });

        await book.Budgets.CopyMonthAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), months: 1);

        (await book.Budgets.GetMonthAsync(new DateOnly(2026, 2, 1))).Lines.Single()
            .Budgeted.ShouldBe(Money.FromDecimal(999m));

        await book.Budgets.CopyMonthAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), months: 1, overwriteExisting: true);

        (await book.Budgets.GetMonthAsync(new DateOnly(2026, 2, 1))).Lines.Single()
            .Budgeted.ShouldBe(Money.FromDecimal(400m));
    }

    [Fact]
    public async Task Rollover_carries_through_the_real_book()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int travel = await book.CategoryIdAsync("Leisure : Travel");

        foreach (int month in new[] { 1, 2, 3 })
        {
            await book.Budgets.SetAsync(new BudgetDraft
            {
                CategoryId = travel,
                PeriodStart = new DateOnly(2026, month, 1),
                Amount = Money.FromDecimal(100m),
                RollsOver = true,
            });
        }

        await book.AddTransactionAsync(account, -40m, new DateOnly(2026, 2, 10), categoryId: travel);

        IReadOnlyList<BudgetPeriodResult> series = await book.Budgets.GetSeriesAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1));

        series[2].Lines[0].RolloverIn.ShouldBe(Money.FromDecimal(160m));
        series[2].Lines[0].Available.ShouldBe(Money.FromDecimal(260m));
    }

    [Fact]
    public async Task A_budget_can_be_cleared()
    {
        using var book = new BookHarness();
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries, PeriodStart = March, Amount = Money.FromDecimal(400m),
        });

        await book.Budgets.ClearAsync(groceries, March);

        (await book.Budgets.GetMonthAsync(March)).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task Budgeting_a_category_that_is_gone_is_refused()
    {
        using var book = new BookHarness();

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Budgets.SetAsync(new BudgetDraft
            {
                CategoryId = 9_999, PeriodStart = March, Amount = Money.FromDecimal(400m),
            }));

        thrown.Errors.ShouldContain(e => e.Code == BudgetService.CategoryNotFound);
    }

    [Fact]
    public async Task A_watched_category_reports_how_it_is_tracking()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        await book.Budgets.ToggleWatchAsync(coffee);

        await book.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = coffee, PeriodStart = March, Amount = Money.FromDecimal(60m),
        });

        await book.AddTransactionAsync(account, -45m, new DateOnly(2026, 3, 10), categoryId: coffee);

        SpendingWatchItem item = (await book.Budgets.GetWatchListAsync(March)).Single();

        item.CategoryPath.ShouldBe("Food : Coffee");
        item.Spent.ShouldBe(Money.FromDecimal(45m));
        item.Budgeted.ShouldBe(Money.FromDecimal(60m));
        item.Remaining.ShouldBe(Money.FromDecimal(15m));
        item.IsOverBudget.ShouldBeFalse();
        item.Progress.ShouldBe(0.75m);
    }

    [Fact]
    public async Task Watching_a_category_twice_stops_watching_it()
    {
        using var book = new BookHarness();
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        (await book.Budgets.ToggleWatchAsync(coffee)).ShouldBeTrue();
        (await book.Budgets.ToggleWatchAsync(coffee)).ShouldBeFalse();

        (await book.Budgets.GetWatchListAsync(March)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_watched_category_with_no_budget_still_reports_what_was_spent()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        await book.Budgets.ToggleWatchAsync(coffee);
        await book.AddTransactionAsync(account, -45m, new DateOnly(2026, 3, 10), categoryId: coffee);

        SpendingWatchItem item = (await book.Budgets.GetWatchListAsync(March)).Single();

        item.HasBudget.ShouldBeFalse();
        item.Spent.ShouldBe(Money.FromDecimal(45m));
        item.Remaining.ShouldBeNull();
    }
}
