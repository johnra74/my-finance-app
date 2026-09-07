using MyFinance.Core.Primitives;
using MyFinance.Import.Ofx;

namespace MyFinance.Import.Tests.Ofx;

public sealed class OfxEntityTests
{
    [Theory]
    [InlineData("AT&amp;T", "AT&T")]
    [InlineData("&lt;tag&gt;", "<tag>")]
    [InlineData("O&apos;Brien", "O'Brien")]
    [InlineData("&quot;quoted&quot;", "\"quoted\"")]
    [InlineData("Caf&#233;", "Café")]
    [InlineData("Caf&#xE9;", "Café")]
    public void Recognised_references_are_expanded(string input, string expected) =>
        OfxValueParser.DecodeEntities(input).ShouldBe(expected);

    [Theory]
    [InlineData("AT&T", "AT&T")]
    [InlineData("Barnes & Noble", "Barnes & Noble")]
    [InlineData("Cash & carry &", "Cash & carry &")]
    [InlineData("A&notarealentity;B", "A&notarealentity;B")]
    public void A_bare_ampersand_survives_untouched(string input, string expected)
    {
        // Unescaped ampersands are routine in OFX 1.x. Treating one as an error would refuse
        // a perfectly ordinary statement over a merchant called "AT&T".
        OfxValueParser.DecodeEntities(input).ShouldBe(expected);
    }

    [Fact]
    public void Decoding_happens_once_and_not_repeatedly()
    {
        // Some issuers double-encode. Expanding twice would turn a merchant genuinely called
        // "A&amp;B" into "A&B" — corrupting a name that had arrived correctly.
        OfxValueParser.DecodeEntities("A&amp;amp;B").ShouldBe("A&amp;B");
    }

    [Fact]
    public void An_unterminated_reference_does_not_swallow_the_rest_of_the_name()
    {
        OfxValueParser.DecodeEntities("SMITH & SONS HARDWARE COMPANY LIMITED")
            .ShouldBe("SMITH & SONS HARDWARE COMPANY LIMITED");
    }

    [Theory]
    [InlineData("&#xD800;")]
    [InlineData("&#0;")]
    [InlineData("&#99999999;")]
    public void An_out_of_range_numeric_reference_is_left_alone(string input) =>
        OfxValueParser.DecodeEntities(input).ShouldBe(input);
}

public sealed class OfxTimestampTests
{
    [Theory]
    [InlineData("20260115")]
    [InlineData("20260115120000")]
    [InlineData("20260115120000.000")]
    [InlineData("20260115120000[-5:EST]")]
    [InlineData("20260115120000.000[-5:EST]")]
    [InlineData("20260115120000[0:GMT]")]
    [InlineData("20260115120000[+5.50:IST]")]
    public void Every_shape_the_specification_allows_yields_the_same_date(string input)
    {
        OfxValueParser.TryParseTimestamp(input, out OfxTimestamp stamp).ShouldBeTrue();
        stamp.Date.ShouldBe(new DateOnly(2026, 1, 15));
    }

    [Fact]
    public void The_stated_time_zone_is_recorded_but_never_applied()
    {
        // 19:00 at UTC-5 is the next day in UTC. Converting would move the transaction to
        // the 2nd, which desynchronises the register from the paper statement being
        // reconciled and silently breaks duplicate matching against earlier imports.
        OfxValueParser.TryParseTimestamp("20260101190000[-5:EST]", out OfxTimestamp stamp)
            .ShouldBeTrue();

        stamp.Date.ShouldBe(new DateOnly(2026, 1, 1));
        stamp.UtcOffsetHours.ShouldBe(-5m);
        stamp.ZoneName.ShouldBe("EST");
        stamp.Time.ShouldBe(new TimeOnly(19, 0, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026011")]
    [InlineData("20261315")]
    [InlineData("20260230")]
    [InlineData("not a date")]
    public void Anything_unreadable_reports_failure_rather_than_throwing(string? input) =>
        OfxValueParser.TryParseTimestamp(input, out _).ShouldBeFalse();

    [Fact]
    public void A_leap_day_is_accepted_in_a_leap_year_and_refused_otherwise()
    {
        OfxValueParser.TryParseDate("20240229", out DateOnly leap).ShouldBeTrue();
        leap.ShouldBe(new DateOnly(2024, 2, 29));

        OfxValueParser.TryParseDate("20260229", out _).ShouldBeFalse();
    }
}

public sealed class OfxAmountTests
{
    [Theory]
    [InlineData("-18.40", -18.40)]
    [InlineData("18.40", 18.40)]
    [InlineData("+18.40", 18.40)]
    [InlineData(".50", 0.50)]
    [InlineData("-.50", -0.50)]
    [InlineData("1234", 1234)]
    [InlineData("-0.00", 0)]
    [InlineData(" -18.40 ", -18.40)]
    public void Ordinary_amounts_parse(string input, decimal expected)
    {
        OfxValueParser.TryParseAmount(input, out Money amount, out _).ShouldBeTrue();
        amount.ShouldBe(Money.FromDecimal(expected));
    }

    [Fact]
    public void A_comma_is_a_decimal_point_not_a_thousands_separator()
    {
        // The specification forbids grouping separators and allows a comma as the decimal
        // point, so "12,34" is twelve dollars thirty-four, not one thousand two hundred.
        OfxValueParser.TryParseAmount("12,34", out Money amount, out _).ShouldBeTrue();
        amount.ShouldBe(Money.FromDecimal(12.34m));
    }

    [Fact]
    public void A_malformed_grouped_amount_reads_the_rightmost_separator_as_the_point()
    {
        OfxValueParser.TryParseAmount("1,234.56", out Money amount, out _).ShouldBeTrue();
        amount.ShouldBe(Money.FromDecimal(1234.56m));
    }

    [Fact]
    public void Extra_precision_is_rounded_and_reported()
    {
        OfxValueParser.TryParseAmount("-18.4049", out Money amount, out bool rounded).ShouldBeTrue();

        amount.ShouldBe(Money.FromDecimal(-18.40m));
        rounded.ShouldBeTrue();
    }

    [Fact]
    public void An_amount_that_needs_no_rounding_is_not_flagged()
    {
        OfxValueParser.TryParseAmount("-18.40", out _, out bool rounded).ShouldBeTrue();
        rounded.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("$18.40")]
    [InlineData("eighteen")]
    [InlineData("--18.40")]
    public void Anything_unreadable_reports_failure_rather_than_throwing(string? input) =>
        OfxValueParser.TryParseAmount(input, out _, out _).ShouldBeFalse();
}
