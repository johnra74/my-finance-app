using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Primitives;

/// <summary>
/// What changes about <see cref="Money"/> once it carries a currency.
/// </summary>
/// <remarks>
/// The whole of <see cref="MoneyTests"/> still passes unmodified, which is the real assertion
/// here: a single-currency book behaves exactly as it did. These cover what is new.
/// </remarks>
public class MoneyCurrencyTests
{
    [Fact]
    public void An_amount_that_was_never_told_is_in_the_default_currency()
    {
        // Every amount in every book that predates this.
        Money.FromDecimal(12.34m).Currency.ShouldBe(Currency.Default);
        Money.Zero.Currency.ShouldBe(Currency.Default);
        default(Money).Currency.ShouldBe(Currency.Default);
    }

    [Fact]
    public void A_yen_amount_has_no_minor_units_and_round_trips()
    {
        Money yen = Money.FromDecimal(1500m, Currency.Jpy);

        // ¥1500 is 1500 yen, not 150,000 of anything.
        yen.MinorUnits.ShouldBe(1500);
        yen.ToDecimal().ShouldBe(1500m);
        yen.Currency.ShouldBe(Currency.Jpy);
    }

    [Fact]
    public void A_three_decimal_amount_round_trips()
    {
        Money dinar = Money.FromDecimal(1.234m, Currency.Kwd);

        dinar.MinorUnits.ShouldBe(1234);
        dinar.ToDecimal().ShouldBe(1.234m);
    }

    [Fact]
    public void A_dollar_amount_is_unchanged_by_any_of_this()
    {
        Money dollars = Money.FromDecimal(12.34m);

        dollars.MinorUnits.ShouldBe(1234);
        dollars.ToDecimal().ShouldBe(12.34m);
    }

    [Fact]
    public void Whole_units_scale_by_the_currency()
    {
        Money.FromUnits(5, Currency.Usd).MinorUnits.ShouldBe(500);
        Money.FromUnits(5, Currency.Jpy).MinorUnits.ShouldBe(5);
        Money.FromUnits(5, Currency.Kwd).MinorUnits.ShouldBe(5000);
    }

    // -- Refusing to mix ----------------------------------------------------------------

    [Fact]
    public void Adding_two_currencies_throws_rather_than_returning_a_number()
    {
        Money dollars = Money.FromDecimal(100m, Currency.Usd);
        Money pounds = Money.FromDecimal(100m, Currency.Gbp);

        // Not coerced, not the left operand, not zero. A silent answer here would be the same
        // bug this change exists to close, one layer deeper.
        Should.Throw<InvalidOperationException>(() => dollars + pounds)
            .Message.ShouldContain("GBP");

        Should.Throw<InvalidOperationException>(() => dollars - pounds);
    }

    [Fact]
    public void Comparing_two_currencies_throws()
    {
        Money dollars = Money.FromDecimal(100m, Currency.Usd);
        Money pounds = Money.FromDecimal(100m, Currency.Gbp);

        // "Is a hundred dollars more than a hundred pounds" has no answer without a rate,
        // and there are no rates.
        Should.Throw<InvalidOperationException>(() => dollars < pounds);
        Should.Throw<InvalidOperationException>(() => dollars.CompareTo(pounds));
    }

    [Fact]
    public void Sorting_a_mixed_list_is_caught_rather_than_faked()
    {
        List<Money> mixed =
        [
            Money.FromDecimal(5m, Currency.Usd),
            Money.FromDecimal(3m, Currency.Gbp),
        ];

        Should.Throw<InvalidOperationException>(() => mixed.Sort());
    }

    [Fact]
    public void Two_currencies_are_never_equal_however_equal_their_numbers()
    {
        // Equality is answerable across currencies where ordering is not: they are simply
        // not the same amount.
        Money.FromDecimal(100m, Currency.Usd)
            .ShouldNotBe(Money.FromDecimal(100m, Currency.Gbp));
    }

    [Fact]
    public void Summing_a_mixed_sequence_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => Money.Sum(
        [
            Money.FromDecimal(5m, Currency.Usd),
            Money.FromDecimal(3m, Currency.Gbp),
        ]));
    }

    // -- Zero, which has to work everywhere ---------------------------------------------

    [Fact]
    public void Zero_absorbs_into_any_currency()
    {
        Money pounds = Money.FromDecimal(10m, Currency.Gbp);

        (Money.Zero + pounds).ShouldBe(pounds);
        (Money.Zero + pounds).Currency.ShouldBe(Currency.Gbp);
        (pounds - pounds).IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Summing_an_empty_sequence_is_still_zero()
    {
        Money.Sum([]).ShouldBe(Money.Zero);
    }

    [Fact]
    public void Summing_one_currency_keeps_it()
    {
        Money total = Money.Sum(
        [
            Money.FromDecimal(1m, Currency.Gbp),
            Money.FromDecimal(2m, Currency.Gbp),
        ]);

        total.Currency.ShouldBe(Currency.Gbp);
        total.ToDecimal().ShouldBe(3m);
    }

    [Fact]
    public void An_unstamped_zero_equals_an_explicitly_stamped_one()
    {
        // A record struct would have compared the currency field and said no.
        Money.Zero.ShouldBe(Money.FromDecimal(0m, Currency.Gbp));
        Money.Zero.GetHashCode().ShouldBe(Money.FromDecimal(0m, Currency.Gbp).GetHashCode());
    }

    // -- The currency travels with the amount -------------------------------------------

    [Fact]
    public void Arithmetic_keeps_the_currency()
    {
        Money pounds = Money.FromDecimal(10m, Currency.Gbp);

        (pounds + pounds).Currency.ShouldBe(Currency.Gbp);
        (pounds * 3).Currency.ShouldBe(Currency.Gbp);
        (-pounds).Currency.ShouldBe(Currency.Gbp);
        pounds.Abs().Currency.ShouldBe(Currency.Gbp);
        pounds.Negated().Currency.ShouldBe(Currency.Gbp);
    }

    [Fact]
    public void Allocation_keeps_the_currency_and_still_loses_nothing()
    {
        Money[] shares = Money.FromDecimal(10m, Currency.Gbp).Allocate(3);

        shares.ShouldAllBe(s => s.Currency == Currency.Gbp);
        Money.Sum(shares).ShouldBe(Money.FromDecimal(10m, Currency.Gbp));
    }

    [Fact]
    public void Weighted_allocation_keeps_the_currency()
    {
        Money[] shares = Money.FromDecimal(10m, Currency.Jpy).Allocate(1, 2);

        shares.ShouldAllBe(s => s.Currency == Currency.Jpy);
        Money.Sum(shares).ShouldBe(Money.FromDecimal(10m, Currency.Jpy));
    }

    [Fact]
    public void Reading_a_stored_count_as_a_currency_reinterprets_rather_than_converts()
    {
        // What the storage layer does with a count and a code from adjacent columns.
        Money stored = Money.FromMinorUnits(1500).In(Currency.Jpy);

        stored.MinorUnits.ShouldBe(1500);
        stored.ToDecimal().ShouldBe(1500m);
        stored.Currency.ShouldBe(Currency.Jpy);
    }
}
