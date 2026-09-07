using System.Collections.Frozen;

namespace MyFinance.Core.Primitives;

/// <summary>
/// A currency, and — the part that matters here — how many minor units it has.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Money"/> held a hard-coded 100 minor units per major one, which is right for
/// the dollar and wrong for the yen, which has none, and for the dinar, which has a thousand.
/// A currency is therefore not decoration on an amount: it is the scale the amount is counted
/// in, and an amount that does not know its own scale cannot be formatted, parsed or compared
/// correctly.
/// </para>
/// <para>
/// This application does not convert between currencies and holds no exchange rates. See
/// `specs/015-multi-currency`: totals spanning more than one currency are reported per
/// currency rather than summed, which is what removes the whole class of failure a stale rate
/// brings with it.
/// </para>
/// </remarks>
public sealed record Currency
{
    /// <summary>ISO 4217 alphabetic code, upper case.</summary>
    public required string Code { get; init; }

    /// <summary>
    /// How many decimal places the currency has: 2 for most, 0 for the yen, 3 for several.
    /// </summary>
    public required int MinorUnitExponent { get; init; }

    /// <summary>Minor units in one major unit — 100 for the dollar, 1 for the yen.</summary>
    public long MinorUnitsPerUnit => Pow10[MinorUnitExponent];

    private static readonly long[] Pow10 = [1, 10, 100, 1000, 10_000];

    /// <summary>
    /// The currency a book uses when it has never been told otherwise.
    /// </summary>
    /// <remarks>
    /// Every amount that predates this type is in this currency, so it is what an unstamped
    /// <see cref="Money"/> is taken to be. Changing it would silently rescale every existing
    /// book.
    /// </remarks>
    public static Currency Default => Usd;

    public static Currency Usd { get; } = new() { Code = "USD", MinorUnitExponent = 2 };

    public static Currency Eur { get; } = new() { Code = "EUR", MinorUnitExponent = 2 };

    public static Currency Gbp { get; } = new() { Code = "GBP", MinorUnitExponent = 2 };

    /// <summary>No minor unit at all: ¥100 is a hundred yen, not one yen.</summary>
    public static Currency Jpy { get; } = new() { Code = "JPY", MinorUnitExponent = 0 };

    /// <summary>Three decimal places, which the hard-coded 100 got wrong by a factor of ten.</summary>
    public static Currency Kwd { get; } = new() { Code = "KWD", MinorUnitExponent = 3 };

    private static readonly FrozenDictionary<string, Currency> Known =
        new Dictionary<string, Currency>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = Usd,
            ["EUR"] = Eur,
            ["GBP"] = Gbp,
            ["CAD"] = new() { Code = "CAD", MinorUnitExponent = 2 },
            ["AUD"] = new() { Code = "AUD", MinorUnitExponent = 2 },
            ["CHF"] = new() { Code = "CHF", MinorUnitExponent = 2 },
            ["JPY"] = Jpy,
            ["KRW"] = new() { Code = "KRW", MinorUnitExponent = 0 },
            ["KWD"] = Kwd,
            ["BHD"] = new() { Code = "BHD", MinorUnitExponent = 3 },
            ["TND"] = new() { Code = "TND", MinorUnitExponent = 3 },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The currency for a code, or a two-place currency of that name when it is not one this
    /// build knows.
    /// </summary>
    /// <remarks>
    /// Deliberately not a refusal. An unknown code is far more likely to be a currency nobody
    /// has added to the table yet than a mistake, and two places is right for all but a
    /// handful — whereas throwing would make an unrecognised code unable to open its own book.
    /// </remarks>
    public static Currency Of(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Default;
        }

        string trimmed = code.Trim();

        return Known.TryGetValue(trimmed, out Currency? known)
            ? known
            : new Currency { Code = trimmed.ToUpperInvariant(), MinorUnitExponent = 2 };
    }

    /// <summary>Every currency this build knows the minor units of.</summary>
    public static IReadOnlyCollection<Currency> All => Known.Values;

    public bool Equals(Currency? other) =>
        other is not null && string.Equals(Code, other.Code, StringComparison.Ordinal);

    public override int GetHashCode() => Code.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Code;
}
