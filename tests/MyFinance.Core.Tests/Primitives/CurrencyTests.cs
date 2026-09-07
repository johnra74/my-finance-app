using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Primitives;

/// <summary>
/// A currency is the scale an amount is counted in, not decoration on it.
/// </summary>
public class CurrencyTests
{
    [Fact]
    public void A_currency_knows_its_minor_unit_exponent()
    {
        Currency.Usd.MinorUnitExponent.ShouldBe(2);
        Currency.Usd.MinorUnitsPerUnit.ShouldBe(100);
    }

    [Fact]
    public void Yen_has_no_minor_units()
    {
        // The case the hard-coded 100 got wrong by a factor of a hundred.
        Currency.Jpy.MinorUnitExponent.ShouldBe(0);
        Currency.Jpy.MinorUnitsPerUnit.ShouldBe(1);
    }

    [Fact]
    public void A_three_decimal_currency_is_expressible()
    {
        Currency.Kwd.MinorUnitExponent.ShouldBe(3);
        Currency.Kwd.MinorUnitsPerUnit.ShouldBe(1000);
    }

    [Fact]
    public void The_default_is_the_currency_every_existing_amount_is_in()
    {
        // Changing this would silently rescale every book already on disk.
        Currency.Default.ShouldBe(Currency.Usd);
    }

    [Theory]
    [InlineData("USD", 2)]
    [InlineData("usd", 2)]
    [InlineData(" GBP ", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 3)]
    public void A_known_code_resolves_to_its_real_scale(string code, int exponent) =>
        Currency.Of(code).MinorUnitExponent.ShouldBe(exponent);

    [Fact]
    public void An_unknown_code_is_taken_at_two_places_rather_than_refused()
    {
        // Far more likely to be a currency nobody has added to the table than a mistake, and
        // throwing would leave a book unable to open itself.
        Currency unknown = Currency.Of("XYZ");

        unknown.Code.ShouldBe("XYZ");
        unknown.MinorUnitExponent.ShouldBe(2);
    }

    [Fact]
    public void No_code_at_all_is_the_default()
    {
        Currency.Of(null).ShouldBe(Currency.Default);
        Currency.Of("   ").ShouldBe(Currency.Default);
    }

    [Fact]
    public void Currencies_are_equal_by_code()
    {
        Currency.Of("USD").ShouldBe(Currency.Usd);
        Currency.Of("usd").ShouldBe(Currency.Usd);
        Currency.Of("GBP").ShouldNotBe(Currency.Usd);
    }
}
