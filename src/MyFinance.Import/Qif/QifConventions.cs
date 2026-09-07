using System.Globalization;

namespace MyFinance.Import.Qif;

/// <summary>Which way round a file writes its day and month.</summary>
public enum QifDateOrder
{
    /// <summary>Nothing in the file settled it.</summary>
    Unknown = 0,

    /// <summary>03/02/2026 is the second of March.</summary>
    MonthFirst = 1,

    /// <summary>03/02/2026 is the third of February.</summary>
    DayFirst = 2,
}

/// <summary>Which character a file uses as its decimal point.</summary>
public enum QifDecimalMark
{
    Unknown = 0,

    /// <summary>1,234.56 — the separator is a full stop.</summary>
    Point = 1,

    /// <summary>1.234,56 — the separator is a comma.</summary>
    Comma = 2,
}

/// <summary>
/// What a whole QIF file's numbers and dates turned out to mean.
/// </summary>
/// <remarks>
/// QIF records no metadata at all: no version, no encoding, no locale. "03/02/2026" is the
/// second of March in an American export and the third of February in a European one, and
/// "1,234" is either one thousand or one and a bit. Neither can be settled from a single
/// value, so the whole file is scanned first and the convention decided from the evidence
/// across every record before a single transaction is built.
/// </remarks>
/// <param name="DateOrder">Whether the day or the month comes first.</param>
/// <param name="DecimalMark">Which character separates the fractional part.</param>
/// <param name="DateOrderWasAmbiguous">
/// True when no date in the file had a component above twelve, so the order was assumed
/// rather than proved.
/// </param>
/// <param name="DateOrderConflicted">
/// True when different dates implied different orders, which means the file is malformed.
/// </param>
public sealed record QifConventions(
    QifDateOrder DateOrder,
    QifDecimalMark DecimalMark,
    bool DateOrderWasAmbiguous,
    bool DateOrderConflicted)
{
    /// <summary>The assumption used when a file gives no evidence either way.</summary>
    public static QifDateOrder DefaultDateOrder =>
        CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern
            .TrimStart('\'', ' ')
            .StartsWith('d')
            ? QifDateOrder.DayFirst
            : QifDateOrder.MonthFirst;

    public QifDateOrder EffectiveDateOrder =>
        DateOrder == QifDateOrder.Unknown ? DefaultDateOrder : DateOrder;

    public QifDecimalMark EffectiveDecimalMark =>
        DecimalMark == QifDecimalMark.Unknown ? QifDecimalMark.Point : DecimalMark;
}

/// <summary>
/// Works out a file's date and number conventions by looking at all of it at once.
/// </summary>
public static class QifConventionDetector
{
    /// <summary>
    /// Decides how to read the file's dates and amounts.
    /// </summary>
    /// <param name="dateValues">Every raw date string in the file.</param>
    /// <param name="amountValues">Every raw amount string in the file.</param>
    public static QifConventions Detect(
        IEnumerable<string> dateValues,
        IEnumerable<string> amountValues)
    {
        ArgumentNullException.ThrowIfNull(dateValues);
        ArgumentNullException.ThrowIfNull(amountValues);

        bool dayFirstProved = false;
        bool monthFirstProved = false;

        foreach (string value in dateValues)
        {
            if (!TrySplitDate(value, out int first, out int second, out _))
            {
                continue;
            }

            // A component above twelve cannot be a month, which settles the order outright.
            if (first > 12)
            {
                dayFirstProved = true;
            }

            if (second > 12)
            {
                monthFirstProved = true;
            }
        }

        QifDateOrder order = (dayFirstProved, monthFirstProved) switch
        {
            (true, false) => QifDateOrder.DayFirst,
            (false, true) => QifDateOrder.MonthFirst,

            // Both proved means the file contradicts itself; neither means every date is
            // genuinely ambiguous. Either way there is nothing to conclude.
            _ => QifDateOrder.Unknown,
        };

        return new QifConventions(
            order,
            DetectDecimalMark(amountValues),
            DateOrderWasAmbiguous: !dayFirstProved && !monthFirstProved,
            DateOrderConflicted: dayFirstProved && monthFirstProved);
    }

    /// <summary>
    /// Decides which character is the decimal point.
    /// </summary>
    /// <remarks>
    /// A value carrying both separators settles it outright — the rightmost is the decimal
    /// point. Failing that, a separator followed by exactly two digits is a decimal point,
    /// while one followed by exactly three is grouping. "1,234" alone proves nothing, which
    /// is why the whole file is consulted.
    /// </remarks>
    private static QifDecimalMark DetectDecimalMark(IEnumerable<string> amountValues)
    {
        int pointVotes = 0;
        int commaVotes = 0;

        foreach (string raw in amountValues)
        {
            string value = raw.Trim();

            int lastPoint = value.LastIndexOf('.');
            int lastComma = value.LastIndexOf(',');

            if (lastPoint >= 0 && lastComma >= 0)
            {
                if (lastPoint > lastComma)
                {
                    pointVotes += 10;
                }
                else
                {
                    commaVotes += 10;
                }

                continue;
            }

            if (lastPoint >= 0 && value.Count(c => c == '.') == 1)
            {
                int digits = CountTrailingDigits(value, lastPoint);
                if (digits is 1 or 2)
                {
                    pointVotes++;
                }
                else if (digits == 3)
                {
                    // Grouping with a full stop implies the comma is the decimal point.
                    commaVotes++;
                }
            }

            if (lastComma >= 0 && value.Count(c => c == ',') == 1)
            {
                int digits = CountTrailingDigits(value, lastComma);
                if (digits is 1 or 2)
                {
                    commaVotes++;
                }
                else if (digits == 3)
                {
                    pointVotes++;
                }
            }
        }

        if (pointVotes == commaVotes)
        {
            return QifDecimalMark.Unknown;
        }

        return pointVotes > commaVotes ? QifDecimalMark.Point : QifDecimalMark.Comma;
    }

    private static int CountTrailingDigits(string value, int separatorIndex)
    {
        int count = 0;

        for (int i = separatorIndex + 1; i < value.Length && char.IsAsciiDigit(value[i]); i++)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Pulls the three numeric components out of a QIF date, whatever it is punctuated with.
    /// </summary>
    /// <remarks>
    /// Quicken writes dates in several shapes: <c>03/02/2026</c>, <c>3/ 2/26</c> with the
    /// space padding it uses instead of a leading zero, <c>03/02'26</c> where the apostrophe
    /// means the twenty-first century, and <c>03-02-2026</c>.
    /// </remarks>
    internal static bool TrySplitDate(string? text, out int first, out int second, out int year)
    {
        first = second = year = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Span<int> parts = stackalloc int[3];
        int found = 0;
        int current = -1;
        bool apostrophe = false;

        foreach (char character in text)
        {
            if (char.IsAsciiDigit(character))
            {
                current = current < 0 ? character - '0' : (current * 10) + (character - '0');
                continue;
            }

            if (character == '\'')
            {
                apostrophe = true;
            }

            if (current >= 0)
            {
                if (found == 3)
                {
                    return false;
                }

                parts[found++] = current;
                current = -1;
            }
        }

        if (current >= 0 && found < 3)
        {
            parts[found++] = current;
        }

        if (found != 3)
        {
            return false;
        }

        first = parts[0];
        second = parts[1];
        year = parts[2];

        if (year < 100)
        {
            // The apostrophe form is explicitly 20xx. Otherwise a two-digit year follows the
            // usual pivot, which no QIF file will ever exercise for a personal ledger but
            // costs nothing to get right.
            year += apostrophe || year < 70 ? 2000 : 1900;
        }

        return first > 0 && second > 0 && year > 0;
    }
}
