using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Reporting;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// Recording what an investment account holds, and keeping it out of the spending reports.
/// </summary>
public class InvestmentServiceTests
{
    private static async Task<(int Account, int Security)> SetUpAsync(BookHarness harness)
    {
        int account = await harness.Accounts.CreateAsync(new AccountDraft
        {
            Name = "Brokerage",
            Type = AccountType.Brokerage,
            OpeningBalance = Money.FromDecimal(10_000m),
        });

        int security = await harness.Investments.FindOrCreateSecurityAsync("Index Fund", "IDX");

        return (account, security);
    }

    private static InvestmentDraft Buy(int account, int security, decimal units, decimal cost) => new()
    {
        AccountId = account,
        SecurityId = security,
        Date = new DateOnly(2026, 3, 1),
        Activity = InvestmentActivity.Buy,
        Quantity = Quantity.FromDecimal(units),
        Amount = Money.FromDecimal(cost),
    };

    [Fact]
    public async Task A_brokerage_account_is_no_longer_balance_only()
    {
        using var harness = new BookHarness();
        (int account, _) = await SetUpAsync(harness);

        await using MyFinanceDbContext db = harness.CreateContext();
        Account created = await db.Accounts.SingleAsync(a => a.Id == account);

        // It used to arrive as UnsupportedImported: read-only, and excluded from everything.
        created.Type.ShouldBe(AccountType.Brokerage);
        created.IsReadOnly.ShouldBeFalse();
    }

    [Fact]
    public async Task A_buy_decreases_cash_and_increases_the_holding()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 10m, 1000m));

        await using MyFinanceDbContext db = harness.CreateContext();

        Holding holding = await db.Holdings.SingleAsync();
        holding.Quantity.ToDecimal().ShouldBe(10m);
        holding.CostBasis.ToDecimal().ShouldBe(1000m);

        // The cash side is a real register row, so the account balance moved.
        MyFinance.Core.Accounts.AccountSummary summary =
            (await harness.Accounts.GetAccountListAsync()).AllAccounts.Single(a => a.Id == account);

        summary.CurrentBalance.ToDecimal().ShouldBe(9000m);
    }

    [Fact]
    public async Task A_cash_leg_is_an_ordinary_transaction_with_its_splits()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 10m, 1000m));

        await using MyFinanceDbContext db = harness.CreateContext();

        Transaction cash = await db.Transactions.Include(t => t.Splits).SingleAsync();

        // Not a second ledger: it goes through the register path and inherits every invariant.
        cash.Splits.ShouldNotBeEmpty();
        Money.Sum(cash.Splits.Select(s => s.Amount)).ShouldBe(cash.Amount);
        cash.SequenceInDay.ShouldBeGreaterThanOrEqualTo(0);

        InvestmentTransaction record = await db.InvestmentTransactions.SingleAsync();
        record.CashTransactionId.ShouldBe(cash.Id);
    }

    [Fact]
    public async Task A_sell_increases_cash_and_decreases_the_holding()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 10m, 1000m));

        await harness.Investments.RecordAsync(new InvestmentDraft
        {
            AccountId = account,
            SecurityId = security,
            Date = new DateOnly(2026, 4, 1),
            Activity = InvestmentActivity.Sell,
            Quantity = Quantity.FromDecimal(4m),
            Amount = Money.FromDecimal(600m),
        });

        await using MyFinanceDbContext db = harness.CreateContext();

        Holding holding = await db.Holdings.SingleAsync();
        holding.Quantity.ToDecimal().ShouldBe(6m);

        // Cost goes out proportionally — four tenths of £1000 — not at what it sold for.
        holding.CostBasis.ToDecimal().ShouldBe(600m);

        MyFinance.Core.Accounts.AccountSummary summary =
            (await harness.Accounts.GetAccountListAsync()).AllAccounts.Single(a => a.Id == account);

        summary.CurrentBalance.ToDecimal().ShouldBe(9600m);
    }

    [Fact]
    public async Task A_dividend_reaches_the_cash_register()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(new InvestmentDraft
        {
            AccountId = account,
            SecurityId = security,
            Date = new DateOnly(2026, 4, 1),
            Activity = InvestmentActivity.Dividend,
            Amount = Money.FromDecimal(25m),
        });

        MyFinance.Core.Accounts.AccountSummary summary =
            (await harness.Accounts.GetAccountListAsync()).AllAccounts.Single(a => a.Id == account);

        summary.CurrentBalance.ToDecimal().ShouldBe(10_025m);
    }

    [Fact]
    public async Task Selling_more_than_is_held_is_refused()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 5m, 500m));

        await Should.ThrowAsync<BookValidationException>(() =>
            harness.Investments.RecordAsync(new InvestmentDraft
            {
                AccountId = account,
                SecurityId = security,
                Date = new DateOnly(2026, 4, 1),
                Activity = InvestmentActivity.Sell,
                Quantity = Quantity.FromDecimal(6m),
                Amount = Money.FromDecimal(700m),
            }));
    }

    [Fact]
    public async Task An_ordinary_account_cannot_hold_securities()
    {
        using var harness = new BookHarness();
        int everyday = await harness.AddAccountAsync("Everyday");
        int security = await harness.Investments.FindOrCreateSecurityAsync("Index Fund");

        await Should.ThrowAsync<BookValidationException>(() =>
            harness.Investments.RecordAsync(Buy(everyday, security, 1m, 100m)));
    }

    [Fact]
    public async Task A_security_is_created_once_and_found_again()
    {
        using var harness = new BookHarness();

        int first = await harness.Investments.FindOrCreateSecurityAsync("Index Fund", "IDX");
        int second = await harness.Investments.FindOrCreateSecurityAsync("Index Fund");

        second.ShouldBe(first);

        await using MyFinanceDbContext db = harness.CreateContext();
        (await db.Securities.CountAsync()).ShouldBe(1);
    }

    // -- Valuation, and its honesty -----------------------------------------------------

    [Fact]
    public async Task A_holding_with_no_price_is_valued_at_cost_and_says_so()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 10m, 1000m));

        HoldingSummary holding =
            (await harness.Investments.GetHoldingsAsync(account, new DateOnly(2026, 6, 30))).Single();

        holding.Value.Amount.ToDecimal().ShouldBe(1000m);
        holding.Value.IsAtCost.ShouldBeTrue();
        holding.Value.PriceDate.ShouldBeNull();
    }

    [Fact]
    public async Task A_valued_figure_carries_the_date_of_the_price_behind_it()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 10m, 1000m));
        await harness.Investments.SetPriceAsync(security, new DateOnly(2026, 5, 1), Money.FromDecimal(130m));

        HoldingSummary holding =
            (await harness.Investments.GetHoldingsAsync(account, new DateOnly(2026, 6, 30))).Single();

        holding.Value.Amount.ToDecimal().ShouldBe(1300m);
        holding.Value.IsAtCost.ShouldBeFalse();
        holding.Value.PriceDate.ShouldBe(new DateOnly(2026, 5, 1));
        holding.Value.DaysOld(new DateOnly(2026, 6, 30)).ShouldBe(60);
    }

    [Fact]
    public async Task Setting_a_price_twice_for_a_day_replaces_it()
    {
        using var harness = new BookHarness();
        (_, int security) = await SetUpAsync(harness);

        await harness.Investments.SetPriceAsync(security, new DateOnly(2026, 5, 1), Money.FromDecimal(100m));
        await harness.Investments.SetPriceAsync(security, new DateOnly(2026, 5, 1), Money.FromDecimal(120m));

        await using MyFinanceDbContext db = harness.CreateContext();
        SecurityPrice price = await db.SecurityPrices.SingleAsync();
        price.Price.ToDecimal().ShouldBe(120m);
    }

    // -- Where holdings show up, and where they must not --------------------------------

    [Fact]
    public async Task Net_worth_includes_the_value_of_holdings()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 10m, 1000m));
        await harness.Investments.SetPriceAsync(security, new DateOnly(2026, 3, 1), Money.FromDecimal(130m));

        ReportOutput output = await harness.Reports.RunAsync(ReportKind.NetWorthOverTime, ReportFilter.Everything with { From = new DateOnly(2026, 3, 1), To = new DateOnly(2026, 3, 31) });

        NetWorthPoint point = output.NetWorth!.Last();

        // £9,000 cash left after the purchase, plus £1,300 of shares.
        point.Assets.ToDecimal().ShouldBe(10_300m);
    }

    [Fact]
    public async Task Net_worth_before_a_purchase_does_not_include_it()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 10m, 1000m));
        await harness.Investments.SetPriceAsync(security, new DateOnly(2026, 1, 1), Money.FromDecimal(130m));

        ReportOutput output = await harness.Reports.RunAsync(ReportKind.NetWorthOverTime, ReportFilter.Everything with { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 31) });

        // Shares bought in March must not appear in a January figure, however early the price
        // was recorded.
        output.NetWorth!.Last().Assets.ToDecimal().ShouldBe(10_000m);
    }

    [Fact]
    public async Task A_share_purchase_never_appears_as_spending()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(Buy(account, security, 10m, 1000m));

        ReportOutput output = await harness.Reports.RunAsync(ReportKind.SpendingByCategory, ReportFilter.Everything with { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) });

        // Buying shares is not spending: the money is still yours, in a different form.
        // Counting it would make every month you invested in look like a month you overspent.
        output.Grouped!.Total.ShouldBe(Money.Zero);
        output.Grouped.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dividend_never_appears_as_income()
    {
        using var harness = new BookHarness();
        (int account, int security) = await SetUpAsync(harness);

        await harness.Investments.RecordAsync(new InvestmentDraft
        {
            AccountId = account,
            SecurityId = security,
            Date = new DateOnly(2026, 4, 1),
            Activity = InvestmentActivity.Dividend,
            Amount = Money.FromDecimal(25m),
        });

        ReportOutput output = await harness.Reports.RunAsync(ReportKind.IncomeByCategory, ReportFilter.Everything with { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) });

        output.Grouped!.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_ordinary_expense_in_a_brokerage_account_still_counts_as_spending()
    {
        // The exclusion is structural — it follows the investment record — not "anything in a
        // brokerage account". A management fee typed as an ordinary transaction is spending.
        using var harness = new BookHarness();
        (int account, _) = await SetUpAsync(harness);

        int groceries = await harness.CategoryIdAsync("Food : Groceries");
        await harness.AddTransactionAsync(account, -40m, new DateOnly(2026, 4, 1), "Shop", groceries);

        ReportOutput output = await harness.Reports.RunAsync(ReportKind.SpendingByCategory, ReportFilter.Everything with { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) });

        output.Grouped!.Rows.ShouldNotBeEmpty();
    }
}
