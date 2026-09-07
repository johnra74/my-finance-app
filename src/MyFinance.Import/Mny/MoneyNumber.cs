namespace MyFinance.Import.Mny;

/// <summary>
/// Reads Microsoft Money's cheque-number field, which is a sort key rather than a number.
/// </summary>
/// <remarks>
/// <para>
/// <c>TRN.szId</c> is not what the user typed. Money prefixes it with a one-character type
/// flag and, for a number, right-aligns the digits in a fixed twelve-character field, so that
/// sorting the column as plain text orders cheques numerically and puts references such as
/// <c>ATM</c> after them. Read verbatim it reaches the register as <c>"0        1168"</c> and
/// <c>"1ATM"</c>.
/// </para>
/// <para>
/// Confirmed against a real 25-year file rather than assumed: every flag-0 value in it was
/// exactly thirteen characters — the flag plus a twelve-wide right-aligned number — across 767
/// occurrences, and every flag-1 value was the flag followed by unpadded text.
/// </para>
/// <para>
/// The rules below are deliberately narrow, because this also has to be safe against a number
/// somebody typed by hand. A cheque numbered <c>1234</c> begins with a <c>1</c>; a rule that
/// simply dropped the first character would turn it into <c>234</c> and say nothing. So the
/// flag is only believed where the rest of the value could not plausibly be anything else —
/// the full thirteen-character padded shape, or a remainder with no digit in it at all.
/// Anything else is returned exactly as it was found.
/// </para>
/// </remarks>
public static class MoneyNumber
{
    /// <summary>The width Money right-aligns a number into, after the flag.</summary>
    private const int PaddedWidth = 12;

    /// <summary>The flag plus that field.</summary>
    private const int PaddedLength = PaddedWidth + 1;

    /// <summary>Strips the sort flag, or returns the value unchanged when it carries none.</summary>
    public static string? Decode(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || stored.Length < 2)
        {
            return stored;
        }

        ReadOnlySpan<char> rest = stored.AsSpan(1).Trim();

        if (rest.IsEmpty)
        {
            return stored;
        }

        return stored[0] switch
        {
            // A number, right-aligned in the fixed field. The length is checked as well as
            // the content: nothing entered by a person is thirteen characters of flag,
            // padding and digits.
            '0' when stored.Length == PaddedLength && IsAllDigits(rest) => rest.ToString(),

            // A reference typed instead of a number. A remainder containing any digit is left
            // alone, because it could be the number itself.
            '1' when !ContainsDigit(rest) => rest.ToString(),

            _ => stored,
        };
    }

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsDigit(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            if (char.IsDigit(c))
            {
                return true;
            }
        }

        return false;
    }
}
