using MyFinance.Core.Accounts;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Accounts;

/// <summary>
/// What the account list totals once a book may hold more than one currency.
/// </summary>
/// <remarks>
/// The whole of <see cref="AccountListBuilderTests"/> still passes unmodified — a
/// single-currency book reads exactly as it always did, which is `SC-004`.
/// </remarks>
public class CurrencyTotalsTests
{
    private static AccountSummary Account(string name, decimal balance, Currency currency, AccountType type = AccountType.Checking) =>
        new()
        {
            Account = new Account { Name = name, Type = type, CurrencyCode = currency.Code },
            CurrentBalance = Money.FromDecimal(balance, currency),
            ClearedBalance = Money.FromDecimal(balance, currency),
            TransactionCount = 1,
            UncategorizedCount = 0,
        };

    [Fact]
    public void One_currency_produces_one_total_that_reads_as_the_grand_total_always_did()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Account("Everyday", 250m, Currency.Usd),
            Account("Savings", 1000m, Currency.Usd, AccountType.Savings),
        ]);

        list.IsSingleCurrency.ShouldBeTrue();
        list.Totals.Count.ShouldBe(1);
        list.Totals[0].Amount.ShouldBe(Money.FromDecimal(1250m));
        list.Total.ShouldBe(Money.FromDecimal(1250m));
    }

    [Fact]
    public void Two_currencies_produce_two_subtotals_and_no_grand_total()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Account("Everyday", 250m, Currency.Usd),
            Account("London", 400m, Currency.Gbp),
        ]);

        list.IsSingleCurrency.ShouldBeFalse();
        list.Totals.Count.ShouldBe(2);

        list.Totals.ShouldContain(t => t.Currency == Currency.Usd && t.Amount.ToDecimal() == 250m);
        list.Totals.ShouldContain(t => t.Currency == Currency.Gbp && t.Amount.ToDecimal() == 400m);

        // There is no rate to add them with, so there is no grand total — and asking for one
        // is an error rather than a number nobody could defend.
        Should.Throw<InvalidOperationException>(() => list.Total);
    }

    [Fact]
    public void The_books_own_currency_is_listed_first()
    {
        // A stable order, so the list does not reshuffle between refreshes.
        AccountListSummary list = AccountListBuilder.Build(
        [
            Account("London", 400m, Currency.Gbp),
            Account("Tokyo", 500m, Currency.Jpy),
            Account("Everyday", 250m, Currency.Usd),
        ]);

        list.Totals[0].Currency.ShouldBe(Currency.Default);
        list.Totals.Skip(1).Select(t => t.Currency.Code).ShouldBe(["GBP", "JPY"]);
    }

    [Fact]
    public void A_group_reports_its_own_currencies()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Account("Everyday", 250m, Currency.Usd),
            Account("London", 400m, Currency.Gbp),
        ]);

        AccountGroupSummary bank = list.Groups.Single(g => g.Group == AccountGroup.Bank);

        bank.Subtotals.Count.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => bank.Subtotal);
    }

    [Fact]
    public void An_empty_list_has_no_totals_and_is_still_single_currency()
    {
        AccountListSummary list = AccountListBuilder.Build([]);

        list.Totals.ShouldBeEmpty();
        list.IsSingleCurrency.ShouldBeTrue();
        list.Total.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Totalling_a_plain_sequence_groups_the_same_way()
    {
        IReadOnlyList<CurrencyTotal> totals = CurrencyTotals.Of(
        [
            Money.FromDecimal(10m, Currency.Gbp),
            Money.FromDecimal(5m, Currency.Gbp),
            Money.FromDecimal(1000m, Currency.Jpy),
        ]);

        totals.Count.ShouldBe(2);
        totals.Single(t => t.Currency == Currency.Gbp).Amount.ToDecimal().ShouldBe(15m);
        totals.Single(t => t.Currency == Currency.Jpy).Amount.ToDecimal().ShouldBe(1000m);
    }
}
