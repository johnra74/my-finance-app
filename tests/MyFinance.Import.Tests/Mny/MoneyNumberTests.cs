using MyFinance.Import.Mny;

namespace MyFinance.Import.Tests.Mny;

/// <summary>
/// Money's cheque-number field is a sort key, and has to be read as one.
/// </summary>
/// <remarks>
/// The register showed <c>"0        1168"</c> and <c>"1ATM"</c> because the field was copied
/// verbatim. Half of what is tested here is that the flag comes off; the other half, and the
/// more important half, is that it comes off <em>only</em> where it is genuinely a flag.
/// </remarks>
public sealed class MoneyNumberTests
{
    /// <summary>Every case the shared rule is judged on, decoded and left alone alike.</summary>
    /// <remarks>
    /// Public because the data layer's repair runs the same list through SQL and the two are
    /// asserted to agree. One rule, stated twice, needs one list of cases.
    /// </remarks>
    public static readonly (string? Stored, string? Expected)[] Cases =
    [
        // The flag-0 shape: twelve characters wide, right-aligned.
        ("0        1168", "1168"),
        ("0         866", "866"),
        ("0            1", "0            1"), // fourteen wide: not the shape, so untouched
        ("0       12345", "12345"),

        // The flag-1 shape: text as typed.
        ("1ATM", "ATM"),
        ("1Deposit", "Deposit"),
        ("1ATM withdrawal", "ATM withdrawal"),

        // Hand-entered numbers. Dropping the first character here would silently corrupt
        // them, which is the whole reason the rules are shaped the way they are.
        ("1234", "1234"),
        ("101", "101"),
        ("1", "1"),
        ("0", "0"),
        ("0123", "0123"),
        ("1A2", "1A2"),

        // Nothing to do.
        (null, null),
        ("", ""),
        ("ATM", "ATM"),
        ("  ", "  "),
        ("1 ", "1 "),
    ];

    public static TheoryData<string?, string?> Table()
    {
        var data = new TheoryData<string?, string?>();

        foreach ((string? stored, string? expected) in Cases)
        {
            data.Add(stored, expected);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void The_sort_flag_is_read_off_and_nothing_else_is_touched(string? stored, string? expected) =>
        MoneyNumber.Decode(stored).ShouldBe(expected);

    [Fact]
    public void A_padded_cheque_number_loses_its_flag_and_padding() =>
        MoneyNumber.Decode("0        1168").ShouldBe("1168");

    [Fact]
    public void A_text_reference_loses_its_flag() =>
        MoneyNumber.Decode("1ATM").ShouldBe("ATM");

    [Fact]
    public void A_hand_typed_number_beginning_with_one_is_left_alone() =>
        MoneyNumber.Decode("1234").ShouldBe("1234");

    [Fact]
    public void Nothing_decoded_still_looks_encoded()
    {
        foreach ((string? stored, _) in Cases)
        {
            string? decoded = MoneyNumber.Decode(stored);

            // Decoding twice must be the same as decoding once, or a value that happens to
            // look encoded again would keep losing a character on every future pass.
            MoneyNumber.Decode(decoded).ShouldBe(decoded);
        }
    }
}
