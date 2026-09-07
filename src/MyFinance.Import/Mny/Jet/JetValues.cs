using System.Globalization;
using System.Text;

namespace MyFinance.Import.Mny.Jet;

/// <summary>
/// Turns the bytes of one column into a .NET value.
/// </summary>
/// <remarks>
/// Kept separate from the page and row walking so the fiddly parts — Jet's compressed
/// unicode, its 1899 date origin, its 10,000-scaled currency — can be tested on their own,
/// without a database anywhere near them.
/// </remarks>
public static class JetValues
{
    /// <summary>Jet counts days from this date.</summary>
    private static readonly DateTime DateOrigin = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Currency is a 64-bit integer with four implied decimal places.</summary>
    public const long CurrencyScale = 10_000L;

    /// <summary>
    /// Widest day count that still lands inside <see cref="DateTime" />.
    /// </summary>
    /// <remarks>
    /// Money writes sentinel dates well outside any real calendar. Clamping rather than
    /// throwing keeps one nonsense date from stopping an entire migration.
    /// </remarks>
    private const double MinimumDays = -657434.0;
    private const double MaximumDays = 2958465.0;

    public static bool TryReadDateTime(ReadOnlySpan<byte> raw, out DateTime value)
    {
        value = default;

        if (raw.Length < 8)
        {
            return false;
        }

        double days = BitConverter.ToDouble(raw);

        if (days == 0 || double.IsNaN(days) || days < MinimumDays || days > MaximumDays)
        {
            return false;
        }

        value = DateOrigin.AddDays(days);
        return true;
    }

    /// <summary>Reads a currency column as its raw 1/10,000 units.</summary>
    public static long ReadCurrencyUnits(ReadOnlySpan<byte> raw) =>
        raw.Length < 8 ? 0L : BitConverter.ToInt64(raw);

    /// <summary>
    /// Reads text, which Jet stores either as plain UTF-16 or in its compressed form.
    /// </summary>
    /// <remarks>
    /// The compressed form starts <c>FF FE</c> and then runs of single-byte characters,
    /// switching back and forth on a <c>00 00</c> marker. It exists because most text in a
    /// real database is Latin-1, and it is why a naive UTF-16 decode of a Money memo comes
    /// out as Chinese.
    /// </remarks>
    public static string ReadText(ReadOnlySpan<byte> raw)
    {
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        {
            return ReadCompressedText(raw[2..]);
        }

        // An odd length cannot be UTF-16; drop the stray byte rather than throw.
        int usable = raw.Length - (raw.Length % 2);
        return Encoding.Unicode.GetString(raw[..usable]).TrimEnd('\0');
    }

    private static string ReadCompressedText(ReadOnlySpan<byte> raw)
    {
        var builder = new StringBuilder(raw.Length);
        bool compressed = true;
        int i = 0;

        while (i < raw.Length)
        {
            if (raw[i] == 0x00 && i + 1 < raw.Length && raw[i + 1] == 0x00)
            {
                compressed = !compressed;
                i += 2;
                continue;
            }

            if (compressed)
            {
                builder.Append((char)raw[i]);
                i++;
            }
            else
            {
                if (i + 1 >= raw.Length)
                {
                    break;
                }

                builder.Append((char)(raw[i] | (raw[i + 1] << 8)));
                i += 2;
            }
        }

        return builder.ToString().TrimEnd('\0');
    }

    /// <summary>Formats a value for a diagnostic message without inventing precision.</summary>
    public static string Describe(object? value) => value switch
    {
        null => "(null)",
        string s => s,
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        byte[] b => $"{b.Length} bytes",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
