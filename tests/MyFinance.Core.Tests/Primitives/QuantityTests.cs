using System.Globalization;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Primitives;

/// <summary>
/// The exact count a holding is measured in.
/// </summary>
/// <remarks>
/// Mirrors <see cref="MoneyTests"/> case for case, because it makes the same promises for the
/// same reason: a holding is built up one purchase at a time, and a count that drifts makes a
/// cost basis that will not reconcile against a broker's statement.
/// </remarks>
public class QuantityTests
{
    [Fact]
    public void A_fractional_share_count_round_trips_without_loss()
    {
        Quantity shares = Quantity.FromDecimal(12.3456m);

        shares.ScaledUnits.ShouldBe(1_234_560_000);
        shares.ToDecimal().ShouldBe(12.3456m);
    }

    [Fact]
    public void Eight_decimal_places_survive()
    {
        Quantity.FromDecimal(0.00000001m).ToDecimal().ShouldBe(0.00000001m);
    }

    [Fact]
    public void Whole_units_convert_exactly()
    {
        Quantity.FromWhole(250).ToDecimal().ShouldBe(250m);
        Quantity.FromWhole(0).ShouldBe(Quantity.Zero);
    }

    [Fact]
    public void Rounds_half_away_from_zero_not_to_even()
    {
        // Banker's rounding would give 0.00000002 for both.
        Quantity.FromDecimal(0.000000025m).ToDecimal().ShouldBe(0.00000003m);
        Quantity.FromDecimal(-0.000000025m).ToDecimal().ShouldBe(-0.00000003m);
    }

    [Fact]
    public void Repeated_addition_stays_exact_where_double_would_drift()
    {
        // The reason this type exists rather than a double.
        Quantity tenth = Quantity.FromDecimal(0.1m);
        Quantity total = Quantity.Zero;

        for (int i = 0; i < 10; i++)
        {
            total += tenth;
        }

        total.ToDecimal().ShouldBe(1m);
        total.ShouldBe(Quantity.FromWhole(1));
    }

    [Fact]
    public void Summing_a_thousand_fractions_is_exact()
    {
        Quantity total = Quantity.Sum(Enumerable.Repeat(Quantity.FromDecimal(0.001m), 1000));

        total.ToDecimal().ShouldBe(1m);
    }

    [Fact]
    public void Sum_of_empty_sequence_is_zero()
    {
        Quantity.Sum([]).ShouldBe(Quantity.Zero);
    }

    [Fact]
    public void Addition_subtraction_and_negation_behave()
    {
        Quantity ten = Quantity.FromWhole(10);
        Quantity three = Quantity.FromWhole(3);

        (ten + three).ToDecimal().ShouldBe(13m);
        (ten - three).ToDecimal().ShouldBe(7m);
        (-ten).ToDecimal().ShouldBe(-10m);
        (ten * 3).ToDecimal().ShouldBe(30m);
        ten.Negated().ShouldBe(-ten);
        (-ten).Abs().ShouldBe(ten);
    }

    [Fact]
    public void Overflow_is_detected_rather_than_wrapping()
    {
        Quantity huge = Quantity.FromScaledUnits(long.MaxValue);

        Should.Throw<OverflowException>(() => huge + Quantity.FromScaledUnits(1));
        Should.Throw<OverflowException>(() => Quantity.FromDecimal(1e12m));
    }

    [Fact]
    public void Sign_helpers_agree_with_the_underlying_value()
    {
        Quantity.FromWhole(5).IsPositive.ShouldBeTrue();
        Quantity.FromWhole(-5).IsNegative.ShouldBeTrue();
        Quantity.Zero.IsZero.ShouldBeTrue();
        Quantity.FromWhole(-5).Sign.ShouldBe(-1);
    }

    [Fact]
    public void Comparison_orders_by_value()
    {
        Quantity small = Quantity.FromDecimal(1.5m);
        Quantity large = Quantity.FromDecimal(1.50000001m);

        (small < large).ShouldBeTrue();
        (large > small).ShouldBeTrue();
        small.CompareTo(large).ShouldBeLessThan(0);

        List<Quantity> sorted = [large, small];
        sorted.Sort();
        sorted[0].ShouldBe(small);
    }

    [Fact]
    public void A_fraction_of_a_holding_is_a_ratio_not_a_count()
    {
        // What a sale apportions cost by.
        Quantity sold = Quantity.FromWhole(25);
        Quantity held = Quantity.FromWhole(100);

        sold.FractionOf(held).ShouldBe(0.25m);
    }

    [Fact]
    public void A_fraction_of_nothing_is_nothing_rather_than_a_divide_by_zero()
    {
        Quantity.FromWhole(5).FractionOf(Quantity.Zero).ShouldBe(0m);
    }

    [Fact]
    public void Formatting_trims_the_zeroes_nobody_typed()
    {
        Quantity.FromDecimal(12.34m).ToString(null, CultureInfo.InvariantCulture).ShouldBe("12.34");
        Quantity.FromWhole(100).ToString(null, CultureInfo.InvariantCulture).ShouldBe("100");
        Quantity.Zero.ToString(null, CultureInfo.InvariantCulture).ShouldBe("0");
    }

    [Theory]
    [InlineData("12.3456", 12.3456)]
    [InlineData("100", 100)]
    [InlineData("-2.5", -2.5)]
    [InlineData("1,000.5", 1000.5)]
    public void Parsing_accepts_what_a_person_would_type(string text, decimal expected)
    {
        Quantity.TryParse(text, CultureInfo.InvariantCulture, out Quantity result).ShouldBeTrue();
        result.ToDecimal().ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("shares")]
    [InlineData(null)]
    public void Parsing_rejects_junk(string? text)
    {
        Quantity.TryParse(text, CultureInfo.InvariantCulture, out Quantity result).ShouldBeFalse();
        result.ShouldBe(Quantity.Zero);
    }
}
