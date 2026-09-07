using System.Globalization;
using System.Text;
using MyFinance.Core.Primitives;

namespace MyFinance.Import.Ofx;

/// <summary>
/// An OFX timestamp: a calendar date, optionally a time, optionally a stated time zone.
/// </summary>
/// <remarks>
/// The offset is carried but deliberately never applied — see
/// <see cref="OfxValueParser.TryParseTimestamp"/> for why.
/// </remarks>
/// <param name="Date">The calendar date exactly as the bank printed it.</param>
/// <param name="Time">Time of day, when the file supplied one.</param>
/// <param name="UtcOffsetHours">Stated offset from GMT, when the file supplied one.</param>
/// <param name="ZoneName">Stated zone abbreviation, e.g. "EST".</param>
public readonly record struct OfxTimestamp(
    DateOnly Date,
    TimeOnly? Time,
    decimal? UtcOffsetHours,
    string? ZoneName);

/// <summary>
/// Parses the scalar value formats OFX uses. Nothing here throws; every method reports
/// failure so one bad field cannot abort a four-hundred-row statement.
/// </summary>
public static class OfxValueParser
{
    /// <summary>
    /// Expands XML/SGML entity references.
    /// </summary>
    /// <remarks>
    /// Lenient by design. An unrecognised or unterminated <c>&amp;</c> is emitted literally
    /// rather than treated as an error, because bare ampersands are routine in OFX 1.x —
    /// "AT&amp;T" and "Barnes &amp; Noble" arrive exactly like that.
    /// <para>
    /// Decoding happens exactly once, never repeatedly. Some issuers double-encode, and
    /// re-running the expansion would turn a merchant genuinely called "A&amp;amp;B" into
    /// "A&amp;B" — silently corrupting a name that was correct.
    /// </para>
    /// </remarks>
    public static string DecodeEntities(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int firstAmp = text.IndexOf('&', StringComparison.Ordinal);
        if (firstAmp < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, firstAmp);

        for (int i = firstAmp; i < text.Length; i++)
        {
            if (text[i] != '&')
            {
                builder.Append(text[i]);
                continue;
            }

            // A reference is short. Scanning the whole rest of the string for a semicolon
            // would let a stray ampersand swallow an entire merchant name.
            int limit = Math.Min(text.Length, i + 12);
            int semicolon = text.IndexOf(';', i + 1);

            if (semicolon < 0 || semicolon >= limit)
            {
                builder.Append('&');
                continue;
            }

            string payload = text[(i + 1)..semicolon];

            if (TryResolveEntity(payload, out string resolved))
            {
                builder.Append(resolved);
                i = semicolon;
            }
            else
            {
                builder.Append('&');
            }
        }

        return builder.ToString();
    }

    private static bool TryResolveEntity(string payload, out string resolved)
    {
        resolved = string.Empty;

        if (payload.Length == 0)
        {
            return false;
        }

        if (payload[0] == '#')
        {
            bool hex = payload.Length > 1 && (payload[1] is 'x' or 'X');
            string digits = hex ? payload[2..] : payload[1..];

            if (digits.Length == 0)
            {
                return false;
            }

            bool parsed = hex
                ? int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)
                : int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);

            // Surrogates and out-of-range values would throw on conversion; emit them
            // literally rather than losing the surrounding text.
            if (!parsed || code <= 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
            {
                return false;
            }

            resolved = char.ConvertFromUtf32(code);
            return true;
        }

        resolved = payload.ToLowerInvariant() switch
        {
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => " ",
            _ => string.Empty,
        };

        return resolved.Length > 0;
    }

    /// <summary>
    /// Parses an OFX timestamp: <c>YYYYMMDD[HHMMSS[.SSS]][[±offset[:ZONE]]]</c>.
    /// </summary>
    /// <remarks>
    /// The stated offset is recorded but <b>not</b> applied to the date, which is why this
    /// returns a <see cref="DateOnly"/> taken straight from the first eight digits.
    /// <para>
    /// Converting "20260101190000[-5:EST]" to UTC would move the transaction to 2 January.
    /// That desynchronises the register from the paper statement the user reconciles
    /// against, and it silently breaks duplicate detection against rows imported before the
    /// rule changed. The bank means "posted on this day at this institution"; take it
    /// literally, as Money and Quicken both do.
    /// </para>
    /// </remarks>
    public static bool TryParseTimestamp(string? text, out OfxTimestamp result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> span = text.Trim();

        decimal? offset = null;
        string? zone = null;

        int bracket = span.IndexOf('[');
        if (bracket >= 0)
        {
            int close = span[bracket..].IndexOf(']');
            ReadOnlySpan<char> inside = close < 0
                ? span[(bracket + 1)..]
                : span.Slice(bracket + 1, close - 1);

            int colon = inside.IndexOf(':');
            ReadOnlySpan<char> offsetText = colon < 0 ? inside : inside[..colon];

            if (decimal.TryParse(offsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsedOffset))
            {
                offset = parsedOffset;
            }

            if (colon >= 0 && inside.Length > colon + 1)
            {
                zone = inside[(colon + 1)..].Trim().ToString();
            }

            span = span[..bracket];
        }

        span = span.Trim();

        if (span.Length < 8)
        {
            return false;
        }

        if (!int.TryParse(span[..4], NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            || !int.TryParse(span.Slice(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month)
            || !int.TryParse(span.Slice(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day))
        {
            return false;
        }

        if (year < 1 || year > 9999 || month < 1 || month > 12
            || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        TimeOnly? time = null;

        if (span.Length >= 14
            && int.TryParse(span.Slice(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hour)
            && int.TryParse(span.Slice(10, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minute)
            && int.TryParse(span.Slice(12, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int second)
            && hour < 24 && minute < 60 && second < 60)
        {
            time = new TimeOnly(hour, minute, second);
        }

        result = new OfxTimestamp(new DateOnly(year, month, day), time, offset, zone);
        return true;
    }

    /// <summary>Convenience wrapper returning just the calendar date.</summary>
    public static bool TryParseDate(string? text, out DateOnly date)
    {
        if (TryParseTimestamp(text, out OfxTimestamp stamp))
        {
            date = stamp.Date;
            return true;
        }

        date = default;
        return false;
    }

    /// <summary>
    /// Parses an OFX amount.
    /// </summary>
    /// <remarks>
    /// The specification forbids grouping separators and permits a comma as the decimal
    /// point, so a comma is treated as a decimal point rather than stripped. Where a
    /// malformed value carries several separators the rightmost wins, which is the only
    /// reading that recovers "1,234.56" correctly.
    /// </remarks>
    /// <param name="rounded">True when the value carried more precision than cents.</param>
    public static bool TryParseAmount(string? text, out Money amount, out bool rounded)
    {
        amount = Money.Zero;
        rounded = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Span<char> buffer = stackalloc char[text.Length];
        int length = 0;
        int lastSeparator = -1;

        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (character is ',' or '.')
            {
                lastSeparator = length;
                buffer[length++] = '.';
                continue;
            }

            if (char.IsAsciiDigit(character) || character is '-' or '+')
            {
                buffer[length++] = character;
                continue;
            }

            // Currency symbols and stray characters are not expected here; anything else
            // means this is not an amount.
            return false;
        }

        if (length == 0)
        {
            return false;
        }

        // Collapse every separator except the rightmost, which is the decimal point.
        Span<char> cleaned = stackalloc char[length];
        int written = 0;

        for (int i = 0; i < length; i++)
        {
            if (buffer[i] == '.' && i != lastSeparator)
            {
                continue;
            }

            cleaned[written++] = buffer[i];
        }

        if (!decimal.TryParse(
                cleaned[..written],
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out decimal value))
        {
            return false;
        }

        decimal atCents = Math.Round(value, 2, MidpointRounding.ToEven);
        rounded = atCents != value;

        try
        {
            amount = Money.FromDecimal(atCents);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
