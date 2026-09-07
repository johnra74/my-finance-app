using System.Globalization;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Primitives;

public class MoneyTests
{
    private static readonly CultureInfo UsCulture = new("en-US");

    [Theory]
    [InlineData("0.00", 0)]
    [InlineData("1.00", 100)]
    [InlineData("1234.56", 123456)]
    [InlineData("-1234.56", -123456)]
    [InlineData("0.01", 1)]
    [InlineData("-0.01", -1)]
    public void FromDecimal_converts_to_exact_minor_units(string input, long expected)
    {
        decimal value = decimal.Parse(input, UsCulture);

        Money.FromDecimal(value).MinorUnits.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.005, 1)]
    [InlineData(0.004, 0)]
    [InlineData(-0.005, -1)]
    [InlineData(0.015, 2)]
    [InlineData(0.025, 3)]
    public void FromDecimal_rounds_half_away_from_zero_not_to_even(decimal input, long expected)
    {
        // 0.025 -> 3 cents proves this is not banker's rounding, which would give 2.
        Money.FromDecimal(input).MinorUnits.ShouldBe(expected);
    }

    [Fact]
    public void FromDecimal_rejects_amounts_beyond_range()
    {
        Should.Throw<OverflowException>(() => Money.FromDecimal(decimal.MaxValue));
    }

    [Fact]
    public void ToDecimal_round_trips_without_loss()
    {
        decimal original = 143957.89m;

        Money.FromDecimal(original).ToDecimal().ShouldBe(original);
    }

    [Fact]
    public void Repeated_addition_stays_exact_where_double_would_drift()
    {
        // 0.1 + 0.2 == 0.3 must hold exactly; in binary floating point it does not.
        Money tenth = Money.FromDecimal(0.1m);
        Money fifth = Money.FromDecimal(0.2m);

        (tenth + fifth).ShouldBe(Money.FromDecimal(0.3m));
    }

    [Fact]
    public void Summing_a_thousand_cents_is_exact()
    {
        Money total = Money.Sum(Enumerable.Repeat(Money.FromMinorUnits(1), 1000));

        total.ShouldBe(Money.FromDecimal(10.00m));
    }

    [Fact]
    public void Sum_of_empty_sequence_is_zero()
    {
        Money.Sum([]).ShouldBe(Money.Zero);
    }

    // -- Arithmetic ---------------------------------------------------------------------

    [Fact]
    public void Addition_subtraction_and_negation_behave()
    {
        Money a = Money.FromDecimal(100.25m);
        Money b = Money.FromDecimal(50.10m);

        (a + b).ShouldBe(Money.FromDecimal(150.35m));
        (a - b).ShouldBe(Money.FromDecimal(50.15m));
        (-a).ShouldBe(Money.FromDecimal(-100.25m));
        (a * 3).ShouldBe(Money.FromDecimal(300.75m));
    }

    [Fact]
    public void Multiplying_by_a_decimal_rate_rounds_away_from_zero()
    {
        Money principal = Money.FromDecimal(1000.00m);

        (principal * 0.0725m).ShouldBe(Money.FromDecimal(72.50m));
    }

    [Fact]
    public void Overflow_is_detected_rather_than_wrapping()
    {
        Should.Throw<OverflowException>(() => Money.MaxValue + Money.FromMinorUnits(1));
    }

    [Fact]
    public void Sign_helpers_agree_with_the_underlying_value()
    {
        Money negative = Money.FromDecimal(-28117.70m);

        negative.IsNegative.ShouldBeTrue();
        negative.IsPositive.ShouldBeFalse();
        negative.IsZero.ShouldBeFalse();
        negative.Sign.ShouldBe(-1);
        negative.Abs().ShouldBe(Money.FromDecimal(28117.70m));
        Money.Zero.IsZero.ShouldBeTrue();
    }

    // -- Allocation ---------------------------------------------------------------------

    [Fact]
    public void Allocate_into_equal_parts_loses_no_cents()
    {
        Money[] parts = Money.FromDecimal(10.00m).Allocate(3);

        parts.Select(p => p.ToDecimal()).ShouldBe([3.34m, 3.33m, 3.33m]);
        Money.Sum(parts).ShouldBe(Money.FromDecimal(10.00m));
    }

    [Fact]
    public void Allocate_handles_negative_totals()
    {
        Money[] parts = Money.FromDecimal(-10.00m).Allocate(3);

        Money.Sum(parts).ShouldBe(Money.FromDecimal(-10.00m));
    }

    [Fact]
    public void Allocate_by_weights_sums_back_to_the_original()
    {
        Money[] parts = Money.FromDecimal(100.00m).Allocate(1, 1, 1);

        Money.Sum(parts).ShouldBe(Money.FromDecimal(100.00m));
        parts.Select(p => p.ToDecimal()).ShouldBe([33.34m, 33.33m, 33.33m]);
    }

    [Fact]
    public void Allocate_by_weights_respects_proportions()
    {
        Money[] parts = Money.FromDecimal(1000.00m).Allocate(70, 30);

        parts.Select(p => p.ToDecimal()).ShouldBe([700.00m, 300.00m]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Allocate_rejects_a_non_positive_part_count(int parts)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Money.FromDecimal(10m).Allocate(parts));
    }

    [Fact]
    public void Allocate_rejects_weights_that_sum_to_zero()
    {
        Should.Throw<ArgumentException>(() => Money.FromDecimal(10m).Allocate(0, 0));
    }

    // -- Comparison ---------------------------------------------------------------------

    [Fact]
    public void Comparison_operators_order_by_value()
    {
        Money small = Money.FromDecimal(-100m);
        Money large = Money.FromDecimal(100m);
        Money smallAgain = Money.FromDecimal(-100m);

        (small < large).ShouldBeTrue();
        (large > small).ShouldBeTrue();
        (small <= smallAgain).ShouldBeTrue();
        (small >= smallAgain).ShouldBeTrue();
        small.CompareTo(large).ShouldBeLessThan(0);
        large.CompareTo(small).ShouldBeGreaterThan(0);
        small.CompareTo(smallAgain).ShouldBe(0);
    }

    [Fact]
    public void Sorting_uses_numeric_order()
    {
        Money[] sorted = new[] { 5m, -10m, 0m, 2.5m }
            .Select(Money.FromDecimal)
            .Order()
            .ToArray();

        sorted.Select(m => m.ToDecimal()).ShouldBe([-10m, 0m, 2.5m, 5m]);
    }

    // -- Formatting ---------------------------------------------------------------------

    [Theory]
    [InlineData(1234.56, "G", "1234.56")]
    [InlineData(-1234.56, "G", "-1234.56")]
    [InlineData(1234.56, "N", "1,234.56")]
    [InlineData(1234.56, "C", "$1,234.56")]
    [InlineData(1234.56, "A", "$1,234.56")]
    [InlineData(-1234.56, "A", "($1,234.56)")]
    [InlineData(0, "A", "$0.00")]
    public void Formatting_matches_the_expected_shape(decimal value, string format, string expected)
    {
        Money.FromDecimal(value).ToString(format, UsCulture).ShouldBe(expected);
    }

    [Fact]
    public void Accounting_format_matches_the_Money_register_convention()
    {
        // Money renders a negative balance in parentheses rather than with a minus sign.
        Money.FromDecimal(-1240.50m).ToAccountingString(UsCulture).ShouldBe("($1,240.50)");
    }

    // -- Parsing ------------------------------------------------------------------------

    [Theory]
    [InlineData("1234.56", 123456)]
    [InlineData("$1,234.56", 123456)]
    [InlineData("(1,234.56)", -123456)]
    [InlineData("($1,234.56)", -123456)]
    [InlineData("-1234.56", -123456)]
    [InlineData("1234.56-", -123456)]
    [InlineData("+1234.56", 123456)]
    [InlineData("  1234.56  ", 123456)]
    [InlineData("0", 0)]
    [InlineData("143957.89", 14395789)]
    public void TryParse_accepts_the_shapes_banks_and_users_produce(string input, long expected)
    {
        Money.TryParse(input, UsCulture, out Money result).ShouldBeTrue();
        result.MinorUnits.ShouldBe(expected);
    }

    [Fact]
    public void TryParse_treats_a_parenthesised_negative_as_a_single_negation()
    {
        // "(-5)" flips twice and lands back on positive rather than double-negating oddly.
        Money.TryParse("(-5.00)", UsCulture, out Money result).ShouldBeTrue();
        result.ShouldBe(Money.FromDecimal(5.00m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    public void TryParse_rejects_junk(string? input)
    {
        Money.TryParse(input, UsCulture, out Money result).ShouldBeFalse();
        result.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Parse_throws_on_junk()
    {
        Should.Throw<FormatException>(() => Money.Parse("not money", UsCulture));
    }

    [Fact]
    public void Parsing_round_trips_the_accounting_format()
    {
        Money original = Money.FromDecimal(-525.25m);

        Money.TryParse(original.ToAccountingString(UsCulture), UsCulture, out Money parsed).ShouldBeTrue();
        parsed.ShouldBe(original);
    }
}
