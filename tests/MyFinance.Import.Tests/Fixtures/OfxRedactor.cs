using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MyFinance.Import.Tests.Fixtures;

/// <summary>
/// Strips the identifying parts out of a real bank download so it can be committed.
/// </summary>
/// <remarks>
/// <para>
/// Works on the raw text rather than through the parser, because the whole value of a real
/// fixture is its malformations — unclosed tags, odd whitespace, a truncated final row. A
/// round trip through the parser would produce a tidy file that no longer reproduces the bug
/// it was kept for. Only the contents of specific leaf tags are rewritten, and every byte
/// around them is left exactly as it was.
/// </para>
/// <para>
/// Amounts and their relationship to the stated closing balance are preserved deliberately:
/// the ledger cross-check that catches a reversed statement is only testable if the numbers
/// still reconcile. A redacted file therefore still shows how much was spent and when — see
/// <c>tests/fixtures/README.md</c>.
/// </para>
/// </remarks>
internal static class OfxRedactor
{
    /// <summary>Tags whose contents identify the account or the institution.</summary>
    private static readonly string[] IdentifierTags =
        ["ACCTID", "BANKID", "BRANCHID", "ACCTKEY", "BROKERID", "USERID"];

    /// <summary>Tags whose contents name a real merchant.</summary>
    private static readonly string[] DescriptorTags = ["NAME", "MEMO", "PAYEE", "ORG"];

    /// <summary>Stand-in merchants, so a redacted file still reads like a statement.</summary>
    private static readonly string[] Merchants =
    [
        "NORTHWIND TRADERS", "CONTOSO MARKET", "FABRIKAM FUEL", "ADVENTURE COFFEE",
        "PROSEWARE PHARMACY", "LITWARE HARDWARE", "TAILSPIN TRANSIT", "WIDE WORLD DELI",
        "GRAPHIC DESIGN CO", "WINGTIP GROCERS", "LUCERNE BAKERY", "TREY TELECOM",
    ];

    /// <summary>
    /// Rewrites the identifying parts of a statement.
    /// </summary>
    /// <param name="text">The raw file contents.</param>
    /// <param name="dateShiftDays">
    /// Days to move every date by. Zero keeps them, which is what the balance fixtures need.
    /// </param>
    public static string Redact(string text, int dateShiftDays = 0)
    {
        ArgumentNullException.ThrowIfNull(text);

        string result = text;

        foreach (string tag in IdentifierTags)
        {
            result = ReplaceTagValues(result, tag, (value, index) => SyntheticId(tag, value));
        }

        result = ReplaceTagValues(result, "FID", (value, index) => "9999");

        foreach (string tag in DescriptorTags)
        {
            // Merchants are assigned by a hash of the original, so the same shop keeps the
            // same stand-in throughout the file and payee matching still behaves realistically.
            result = ReplaceTagValues(result, tag, (value, index) => SyntheticMerchant(value));
        }

        // FITIDs are hashed rather than renumbered, so re-importing a redacted file still
        // exercises duplicate detection exactly as the original would.
        result = ReplaceTagValues(result, "FITID", (value, index) => SyntheticId("FITID", value));
        result = ReplaceTagValues(result, "CORRECTFITID", (value, index) => SyntheticId("FITID", value));

        if (dateShiftDays != 0)
        {
            foreach (string tag in new[] { "DTPOSTED", "DTUSER", "DTAVAIL", "DTSTART", "DTEND", "DTASOF", "DTSERVER" })
            {
                result = ReplaceTagValues(result, tag, (value, index) => ShiftDate(value, dateShiftDays));
            }
        }

        return result;
    }

    /// <summary>
    /// Replaces the text content of every occurrence of one tag, leaving the markup alone.
    /// </summary>
    /// <remarks>
    /// Handles both dialects at once: the value runs from the end of the opening tag to
    /// whichever comes first, the closing tag or the next opening one. That is exactly the
    /// SGML rule, so a file with no closing tags is rewritten just as correctly as XML.
    /// </remarks>
    private static string ReplaceTagValues(string text, string tag, Func<string, int, string> rewrite)
    {
        var builder = new StringBuilder(text.Length);
        string open = "<" + tag + ">";

        int position = 0;
        int occurrence = 0;

        while (true)
        {
            int start = text.IndexOf(open, position, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                builder.Append(text, position, text.Length - position);
                return builder.ToString();
            }

            int valueStart = start + open.Length;
            int valueEnd = text.IndexOf('<', valueStart);

            if (valueEnd < 0)
            {
                builder.Append(text, position, text.Length - position);
                return builder.ToString();
            }

            string original = text[valueStart..valueEnd];

            // Trailing whitespace is part of the file's shape, not the value, so it is kept.
            string trimmed = original.TrimEnd('\r', '\n', ' ', '\t');
            string suffix = original[trimmed.Length..];

            builder.Append(text, position, valueStart - position);
            builder.Append(trimmed.Length == 0 ? original : rewrite(trimmed, occurrence) + suffix);

            position = valueEnd;
            occurrence++;
        }
    }

    private static string SyntheticId(string tag, string original)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(tag + "|" + original));
        string digits = string.Concat(hash.Take(8).Select(b => (b % 10).ToString(CultureInfo.InvariantCulture)));

        // Same length as the original, so column widths and any length-sensitive quirk in the
        // file survive redaction.
        return original.Length <= digits.Length
            ? digits[..original.Length]
            : digits.PadRight(original.Length, '0');
    }

    private static string SyntheticMerchant(string original)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(original));
        string merchant = Merchants[hash[0] % Merchants.Length];

        // Keep any trailing store number shape, since that is what the descriptor cleaner's
        // stable-key handling exists to cope with.
        string tail = string.Concat(original.Reverse().TakeWhile(char.IsAsciiDigit).Reverse());

        return tail.Length >= 3 ? $"{merchant} {tail}" : merchant;
    }

    private static string ShiftDate(string value, int days)
    {
        if (value.Length < 8
            || !DateOnly.TryParseExact(
                value[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date))
        {
            return value;
        }

        return date.AddDays(days).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + value[8..];
    }
}
