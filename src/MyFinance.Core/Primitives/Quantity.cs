using System.Globalization;

namespace MyFinance.Core.Primitives;

/// <summary>
/// An exact count of units held — shares, units of a fund — as a whole number of
/// hundred-millionths.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Money"/> cannot do this job: it counts a currency's minor units, two decimal
/// places for the dollar, and a holding is routinely 12.3456 shares. <see cref="double"/>
/// cannot either — the first constitutional principle rules it out, and for the reason
/// <c>MoneyTests.Repeated_addition_stays_exact_where_double_would_drift</c> demonstrates: a
/// holding is built up by adding one purchase at a time, and a quantity that drifts makes a
/// cost basis that cannot be reconciled against a broker's statement.
/// </para>
/// <para>
/// So: the same shape as <see cref="Money"/>, a <see cref="long"/> of scaled units with
/// integer arithmetic throughout, and the same guarantees — exact addition, half away from
/// zero, overflow detected rather than wrapped.
/// </para>
/// <para>
/// Eight decimal places covers every fractional share a broker issues and every fund unit,
/// with room to spare. It caps a single holding at about 92 billion units, which is not a
/// limit anybody will meet.
/// </para>
/// </remarks>
public readonly record struct Quantity : IComparable<Quantity>, IComparable, IFormattable
{
    /// <summary>Scaled units in one whole unit.</summary>
    public const long UnitsPerWhole = 100_000_000;

    /// <summary>Decimal places in the whole-unit representation.</summary>
    public const int DecimalPlaces = 8;

    private Quantity(long scaledUnits) => ScaledUnits = scaledUnits;

    /// <summary>The exact count, as a signed whole number of hundred-millionths.</summary>
    public long ScaledUnits { get; }

    public static Quantity Zero => default;

    /// <summary>Wraps a raw scaled count. This is the storage-layer entry point.</summary>
    public static Quantity FromScaledUnits(long scaledUnits) => new(scaledUnits);

    /// <summary>Wraps a whole number of units.</summary>
    public static Quantity FromWhole(long units) => new(checked(units * UnitsPerWhole));

    /// <summary>
    /// Converts a decimal count, rounding half away from zero — the same convention
    /// <see cref="Money"/> uses, and notably not <see cref="decimal"/>'s banker's default.
    /// </summary>
    public static Quantity FromDecimal(decimal units)
    {
        decimal scaled = decimal.Round(units * UnitsPerWhole, 0, MidpointRounding.AwayFromZero);

        if (scaled is > 9223372036854775807m or < -9223372036854775808m)
        {
            throw new OverflowException($"Quantity {units} is outside the representable range.");
        }

        return new Quantity((long)scaled);
    }

    /// <summary>The count in whole units. Always exact — no precision is lost.</summary>
    public decimal ToDecimal() => (decimal)ScaledUnits / UnitsPerWhole;

    public bool IsZero => ScaledUnits == 0;

    public bool IsNegative => ScaledUnits < 0;

    public bool IsPositive => ScaledUnits > 0;

    public int Sign => Math.Sign(ScaledUnits);

    public Quantity Abs() => new(Math.Abs(ScaledUnits));

    public Quantity Negated() => new(-ScaledUnits);

    // -- Arithmetic ---------------------------------------------------------------------

    public static Quantity operator +(Quantity left, Quantity right) =>
        new(checked(left.ScaledUnits + right.ScaledUnits));

    public static Quantity operator -(Quantity left, Quantity right) =>
        new(checked(left.ScaledUnits - right.ScaledUnits));

    public static Quantity operator -(Quantity value) => new(checked(-value.ScaledUnits));

    public static Quantity operator +(Quantity value) => value;

    public static Quantity operator *(Quantity left, long factor) =>
        new(checked(left.ScaledUnits * factor));

    public static Quantity operator *(long factor, Quantity right) => right * factor;

    /// <summary>Sums a sequence, returning <see cref="Zero"/> when empty.</summary>
    public static Quantity Sum(IEnumerable<Quantity> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        long total = 0;

        foreach (Quantity value in values)
        {
            total = checked(total + value.ScaledUnits);
        }

        return new Quantity(total);
    }

    /// <summary>
    /// What fraction of a holding this is, for apportioning cost on a sale.
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="decimal"/> rather than a <see cref="Quantity"/> because it is a
    /// ratio, not a count. The caller multiplies a <see cref="Money"/> cost by it, and that
    /// multiplication already rounds half away from zero.
    /// </remarks>
    public decimal FractionOf(Quantity whole) =>
        whole.IsZero ? 0m : (decimal)ScaledUnits / whole.ScaledUnits;

    // -- Comparison ---------------------------------------------------------------------

    public static bool operator <(Quantity left, Quantity right) => left.ScaledUnits < right.ScaledUnits;

    public static bool operator >(Quantity left, Quantity right) => left.ScaledUnits > right.ScaledUnits;

    public static bool operator <=(Quantity left, Quantity right) => left.ScaledUnits <= right.ScaledUnits;

    public static bool operator >=(Quantity left, Quantity right) => left.ScaledUnits >= right.ScaledUnits;

    public int CompareTo(Quantity other) => ScaledUnits.CompareTo(other.ScaledUnits);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        Quantity other => CompareTo(other),
        _ => throw new ArgumentException($"Cannot compare Quantity to {obj.GetType()}.", nameof(obj)),
    };

    // -- Formatting ---------------------------------------------------------------------

    /// <summary>
    /// Trailing zeroes trimmed, because "12.34 shares" is what a person wrote and
    /// "12.34000000" is only what the storage happens to be.
    /// </summary>
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        formatProvider ??= CultureInfo.CurrentCulture;
        decimal value = ToDecimal();

        return (format ?? "G").ToUpperInvariant() switch
        {
            "G" => Trim(value, formatProvider),
            "N" => value.ToString("N8", formatProvider),
            _ => value.ToString(format, formatProvider),
        };
    }

    private static string Trim(decimal value, IFormatProvider provider)
    {
        string text = value.ToString("0.########", provider);
        return text.Length == 0 ? "0" : text;
    }

    public static bool TryParse(string? text, IFormatProvider? formatProvider, out Quantity result)
    {
        result = Zero;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!decimal.TryParse(
                text.Trim(),
                NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                formatProvider ?? CultureInfo.CurrentCulture,
                out decimal parsed))
        {
            return false;
        }

        try
        {
            result = FromDecimal(parsed);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryParse(string? text, out Quantity result) =>
        TryParse(text, CultureInfo.CurrentCulture, out result);
}
