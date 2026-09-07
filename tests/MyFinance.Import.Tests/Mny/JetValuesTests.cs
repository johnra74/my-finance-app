using System.Text;
using MyFinance.Import.Mny.Jet;

namespace MyFinance.Import.Tests.Mny;

public sealed class JetValuesTests
{
    [Fact]
    public void Plain_utf16_text_reads()
    {
        byte[] raw = Encoding.Unicode.GetBytes("Blue Bottle");

        JetValues.ReadText(raw).ShouldBe("Blue Bottle");
    }

    /// <summary>
    /// Jet stores most text one byte per character behind an FF FE marker. Decoding that as
    /// UTF-16 is what turns an English memo into Chinese, so it is worth pinning down.
    /// </summary>
    [Fact]
    public void Compressed_text_reads_one_byte_per_character()
    {
        byte[] raw = [0xFF, 0xFE, .. "Woodgrove Mortgage"u8];

        JetValues.ReadText(raw).ShouldBe("Woodgrove Mortgage");
    }

    [Fact]
    public void A_compressed_run_can_switch_back_to_two_byte_characters()
    {
        // "AB", then the 00 00 marker, then a single wide character.
        byte[] raw = [0xFF, 0xFE, (byte)'A', (byte)'B', 0x00, 0x00, 0xAC, 0x20];

        JetValues.ReadText(raw).ShouldBe("AB€");
    }

    [Fact]
    public void Empty_text_is_empty_rather_than_null()
    {
        JetValues.ReadText([]).ShouldBe(string.Empty);
    }

    [Fact]
    public void An_odd_byte_count_does_not_throw()
    {
        byte[] raw = [.. Encoding.Unicode.GetBytes("Hi"), 0x41];

        JetValues.ReadText(raw).ShouldBe("Hi");
    }

    [Fact]
    public void Currency_is_read_in_ten_thousandths()
    {
        byte[] raw = BitConverter.GetBytes(1234_5600L);

        JetValues.ReadCurrencyUnits(raw).ShouldBe(1234_5600L);
    }

    [Fact]
    public void A_date_counts_days_from_the_thirtieth_of_december_1899()
    {
        byte[] raw = BitConverter.GetBytes(45000.0);

        JetValues.TryReadDateTime(raw, out DateTime value).ShouldBeTrue();
        value.ShouldBe(new DateTime(2023, 3, 15, 0, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void A_zero_date_is_no_date()
    {
        JetValues.TryReadDateTime(BitConverter.GetBytes(0.0), out _).ShouldBeFalse();
    }

    /// <summary>
    /// Money writes sentinel dates far outside any calendar. One of them must not stop a
    /// migration of twenty-five years of records.
    /// </summary>
    [Fact]
    public void A_date_beyond_the_calendar_is_refused_rather_than_thrown()
    {
        JetValues.TryReadDateTime(BitConverter.GetBytes(9.9e12), out _).ShouldBeFalse();
        JetValues.TryReadDateTime(BitConverter.GetBytes(-9.9e12), out _).ShouldBeFalse();
    }
}
