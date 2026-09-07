using System.Globalization;

namespace MyFinance.Core.Primitives;

/// <summary>
/// An exact monetary amount held as a whole number of minor units (cents).
/// </summary>
/// <remarks>
/// <para>
/// Register balances are prefix sums over thousands of rows and must reconcile to the
/// penny against a bank statement, so no binary floating point is involved anywhere:
/// the value is a <see cref="long"/> and every operation is integer arithmetic.
/// </para>
/// <para>
/// <b>An amount carries its currency</b>, because the currency is its scale: a dollar has a
/// hundred minor units, a yen none, a dinar a thousand. An amount that does not know which it
/// is cannot be formatted, parsed or compared correctly — and, worse, can be added to an
/// amount of a different currency to produce a number that means nothing. Adding or comparing
/// across currencies therefore <b>throws</b>: it is a programming error, and the aggregation
/// layer is supposed to make it unreachable by grouping first.
/// </para>
/// <para>
/// The currency is a reference held beside the count, so <c>default(Money)</c> — which is
/// <see cref="Zero"/>, and which a great deal of code relies on — is still valid, and reads as
/// <see cref="Currency.Default"/>. Zero is compatible with every currency, so summing an empty
/// or single-currency sequence needs no special case.
/// </para>
/// <para>
/// This application holds no exchange rates and never converts. See
/// `specs/015-multi-currency`.
/// </para>
/// </remarks>
public readonly record struct Money : IComparable<Money>, IComparable, IFormattable
{
    private readonly Currency? _currency;

    private Money(long minorUnits, Currency? currency = null)
    {
        MinorUnits = minorUnits;
        _currency = currency;
    }

    /// <summary>The exact amount, as a signed whole number of minor units.</summary>
    public long MinorUnits { get; }

    /// <summary>
    /// What the minor units are counted in. <see cref="Currency.Default"/> for an amount that
    /// was never told, which is every amount in a single-currency book.
    /// </summary>
    public Currency Currency => _currency ?? Currency.Default;

    /// <summary>Minor units in one major unit of this amount's currency.</summary>
    public long MinorUnitsPerUnit => Currency.MinorUnitsPerUnit;

    /// <summary>
    /// Zero, in no particular currency.
    /// </summary>
    /// <remarks>
    /// Compatible with every currency: nothing is nothing whatever it is denominated in, and
    /// without that every sum would need a seed of the right currency to start from.
    /// </remarks>
    public static Money Zero => default;

    public static Money MaxValue => new(long.MaxValue);

    public static Money MinValue => new(long.MinValue);

    /// <summary>Wraps a raw minor-unit count. This is the storage-layer entry point.</summary>
    public static Money FromMinorUnits(long minorUnits) => new(minorUnits);

    public static Money FromMinorUnits(long minorUnits, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(minorUnits, currency);
    }

    /// <summary>Wraps a whole number of major units.</summary>
    public static Money FromUnits(long units) => FromUnits(units, Currency.Default);

    public static Money FromUnits(long units, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(checked(units * currency.MinorUnitsPerUnit), currency);
    }

    /// <summary>The same amount, read as the given currency. Does not convert.</summary>
    /// <remarks>
    /// For the storage layer, which reads a count and a code from adjacent columns. It
    /// reinterprets the scale; it does not apply a rate, because there are none.
    /// </remarks>
    public Money In(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(MinorUnits, currency);
    }

    /// <summary>
    /// Converts a decimal amount, rounding half away from zero — the convention every
    /// financial institution uses, and notably not <see cref="decimal"/>'s banker's default.
    /// </summary>
    public static Money FromDecimal(decimal amount) => FromDecimal(amount, Currency.Default);

    public static Money FromDecimal(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        decimal scaled = decimal.Round(
            amount * currency.MinorUnitsPerUnit, 0, MidpointRounding.AwayFromZero);

        if (scaled is > 9223372036854775807m or < -9223372036854775808m)
        {
            throw new OverflowException($"Amount {amount} is outside the representable range of Money.");
        }

        return new Money((long)scaled, currency);
    }

    /// <summary>The amount in major units. Always exact — no precision is lost.</summary>
    public decimal ToDecimal() => (decimal)MinorUnits / MinorUnitsPerUnit;

    public bool IsZero => MinorUnits == 0;

    public bool IsNegative => MinorUnits < 0;

    public bool IsPositive => MinorUnits > 0;

    public int Sign => Math.Sign(MinorUnits);

    public Money Abs() => new(Math.Abs(MinorUnits), _currency);

    public Money Negated() => new(-MinorUnits, _currency);

    // -- Arithmetic ---------------------------------------------------------------------

    public static Money operator +(Money left, Money right) =>
        new(checked(left.MinorUnits + right.MinorUnits), Agreed(left, right, "add"));

    public static Money operator -(Money left, Money right) =>
        new(checked(left.MinorUnits - right.MinorUnits), Agreed(left, right, "subtract"));

    public static Money operator -(Money value) => new(checked(-value.MinorUnits), value._currency);

    public static Money operator +(Money value) => value;

    public static Money operator *(Money left, long factor) =>
        new(checked(left.MinorUnits * factor), left._currency);

    /// <summary>
    /// The currency two amounts share, or a refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero is compatible with anything — nothing is nothing in any currency — which is what
    /// lets a sum start from <see cref="Zero"/> without knowing what it is about to add up.
    /// </para>
    /// <para>
    /// Anything else across two currencies throws. It does not coerce, take the left operand,
    /// or return zero: this is a programming error the aggregation layer is supposed to make
    /// unreachable by grouping first, and a silent answer here would be the same bug this
    /// whole change exists to close, one layer deeper.
    /// </para>
    /// </remarks>
    private static Currency? Agreed(Money left, Money right, string operation)
    {
        if (left.IsZero)
        {
            return right._currency ?? left._currency;
        }

        if (right.IsZero || left.Currency == right.Currency)
        {
            return left._currency ?? right._currency;
        }

        throw new InvalidOperationException(
            $"Cannot {operation} {left.Currency.Code} and {right.Currency.Code}: "
            + "this application does not convert between currencies. Group by currency first.");
    }

    public static Money operator *(long factor, Money right) => right * factor;

    /// <summary>
    /// Scales by a decimal factor (interest rates, percentage splits), rounding half away
    /// from zero. Repeated scaling loses cents by design — use <see cref="Allocate(int)"/>
    /// when a total must be divided without leaking value.
    /// </summary>
    public static Money operator *(Money left, decimal factor) =>
        new((long)decimal.Round(left.MinorUnits * factor, 0, MidpointRounding.AwayFromZero));

    public static Money operator *(decimal factor, Money right) => right * factor;

    /// <summary>Sums a sequence, returning <see cref="Zero"/> when empty.</summary>
    public static Money Sum(IEnumerable<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        Money total = Zero;

        foreach (Money value in values)
        {
            // Through the operator, so a mixed sequence is refused here exactly as it would
            // be by a hand-written a + b.
            total += value;
        }

        return total;
    }

    /// <summary>
    /// Splits this amount into <paramref name="parts"/> shares that sum back to exactly the
    /// original. The remainder cents are distributed one each across the leading shares, so
    /// $10.00 into 3 yields $3.34, $3.33, $3.33 rather than three lossy $3.33s.
    /// </summary>
    public Money[] Allocate(int parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);

        long baseShare = MinorUnits / parts;
        long remainder = MinorUnits - (baseShare * parts);
        int step = Math.Sign(remainder);

        var result = new Money[parts];
        for (int i = 0; i < parts; i++)
        {
            long share = baseShare;
            if (remainder != 0)
            {
                share += step;
                remainder -= step;
            }

            result[i] = new Money(share, _currency);
        }

        return result;
    }

    /// <summary>
    /// Splits this amount across the given integer weights so the shares sum back to exactly
    /// the original, distributing any rounding remainder to the largest weights first.
    /// </summary>
    public Money[] Allocate(params int[] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentOutOfRangeException.ThrowIfZero(weights.Length);

        long totalWeight = 0;
        foreach (int weight in weights)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(weight);
            totalWeight = checked(totalWeight + weight);
        }

        if (totalWeight == 0)
        {
            throw new ArgumentException("Weights must not sum to zero.", nameof(weights));
        }

        var result = new Money[weights.Length];
        long allocated = 0;

        for (int i = 0; i < weights.Length; i++)
        {
            long share = MinorUnits * weights[i] / totalWeight;
            result[i] = new Money(share, _currency);
            allocated += share;
        }

        // Hand out the leftover cents to the heaviest weights, largest first.
        long remainder = MinorUnits - allocated;
        int step = Math.Sign(remainder);

        foreach (int index in Enumerable.Range(0, weights.Length)
                     .OrderByDescending(i => weights[i])
                     .ThenBy(i => i))
        {
            if (remainder == 0)
            {
                break;
            }

            result[index] = new Money(result[index].MinorUnits + step, _currency);
            remainder -= step;
        }

        return result;
    }

    // -- Comparison ---------------------------------------------------------------------

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Orders by value, and refuses to order two currencies against each other.
    /// </summary>
    /// <remarks>
    /// "Is a hundred dollars more than a hundred pounds" has no answer here, because there is
    /// no rate to answer it with. Sorting a mixed list is therefore a mistake to be caught,
    /// not a comparison to be faked.
    /// </remarks>
    public int CompareTo(Money other)
    {
        Agreed(this, other, "compare");
        return MinorUnits.CompareTo(other.MinorUnits);
    }

    /// <summary>
    /// Equality, which unlike ordering is answerable across currencies: they are simply not
    /// equal. A record struct would otherwise compare the currency field, and an unstamped
    /// zero would not equal an explicitly-dollar zero.
    /// </summary>
    public bool Equals(Money other) =>
        MinorUnits == other.MinorUnits
        && (MinorUnits == 0 || Currency == other.Currency);

    public override int GetHashCode() => MinorUnits.GetHashCode();

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        Money other => CompareTo(other),
        _ => throw new ArgumentException($"Cannot compare Money to {obj.GetType()}.", nameof(obj)),
    };

    // -- Formatting ---------------------------------------------------------------------

    /// <summary>Plain signed amount with two decimals, e.g. <c>-1234.56</c>.</summary>
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>
    /// Supported formats: <c>G</c>/null plain signed, <c>C</c> currency, <c>A</c> accounting
    /// (negatives in parentheses, as Microsoft Money renders them), <c>N</c> grouped.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        formatProvider ??= CultureInfo.CurrentCulture;
        decimal value = ToDecimal();

        return (format ?? "G").ToUpperInvariant() switch
        {
            "G" => value.ToString("0.00", formatProvider),
            "N" => value.ToString("N2", formatProvider),
            "C" => value.ToString("C2", formatProvider),
            "A" => IsNegative
                ? $"({Abs().ToDecimal().ToString("C2", formatProvider)})"
                : value.ToString("C2", formatProvider),
            _ => value.ToString(format, formatProvider),
        };
    }

    /// <summary>Accounting form used throughout the register and reports.</summary>
    public string ToAccountingString(IFormatProvider? formatProvider = null) =>
        ToString("A", formatProvider ?? CultureInfo.CurrentCulture);

    // -- Parsing ------------------------------------------------------------------------

    /// <summary>
    /// Parses user and import-file input, accepting currency symbols, thousands separators,
    /// accounting parentheses, a trailing minus (as some QIF and OFX writers emit), and
    /// unicode minus signs.
    /// </summary>
    public static bool TryParse(string? text, IFormatProvider? formatProvider, out Money result)
    {
        result = Zero;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        formatProvider ??= CultureInfo.CurrentCulture;
        ReadOnlySpan<char> span = text.AsSpan().Trim();

        bool negative = false;

        if (span.Length >= 2 && span[0] == '(' && span[^1] == ')')
        {
            negative = true;
            span = span[1..^1].Trim();
        }

        // Trailing sign, e.g. "1234.56-".
        if (span.Length > 0 && (span[^1] == '-' || span[^1] == '−'))
        {
            negative = !negative;
            span = span[..^1].Trim();
        }

        if (span.Length > 0 && (span[0] == '-' || span[0] == '−'))
        {
            negative = !negative;
            span = span[1..].Trim();
        }
        else if (span.Length > 0 && span[0] == '+')
        {
            span = span[1..].Trim();
        }

        const NumberStyles Styles = NumberStyles.AllowThousands
                                    | NumberStyles.AllowDecimalPoint
                                    | NumberStyles.AllowCurrencySymbol
                                    | NumberStyles.AllowLeadingWhite
                                    | NumberStyles.AllowTrailingWhite;

        if (!decimal.TryParse(span, Styles, formatProvider, out decimal parsed) || parsed < 0)
        {
            return false;
        }

        try
        {
            result = FromDecimal(negative ? -parsed : parsed);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryParse(string? text, out Money result) =>
        TryParse(text, CultureInfo.CurrentCulture, out result);

    public static Money Parse(string text, IFormatProvider? formatProvider = null) =>
        TryParse(text, formatProvider, out Money result)
            ? result
            : throw new FormatException($"'{text}' is not a recognisable monetary amount.");
}
