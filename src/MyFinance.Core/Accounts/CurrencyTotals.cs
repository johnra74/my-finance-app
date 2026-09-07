using MyFinance.Core.Primitives;

namespace MyFinance.Core.Accounts;

/// <summary>
/// Totals a sequence per currency rather than across it.
/// </summary>
/// <remarks>
/// The one operation that replaces a grand total once a book may hold more than one currency.
/// It is deliberately not a conversion: this application holds no exchange rates, so amounts
/// in different currencies are reported side by side and never added. See
/// `specs/015-multi-currency`.
/// </remarks>
public static class CurrencyTotals
{
    public static IReadOnlyList<CurrencyTotal> Of<T>(IEnumerable<T> items, Func<T, Money> amount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(amount);

        return
        [
            .. items
                .Select(amount)
                .GroupBy(m => m.Currency)
                .Select(g => new CurrencyTotal(g.Key, Money.Sum(g)))

                // A stable order so the list does not reshuffle between refreshes, with the
                // book's own currency first because it is the one being read.
                .OrderByDescending(t => t.Currency == Currency.Default)
                .ThenBy(t => t.Currency.Code, StringComparer.Ordinal)
        ];
    }

    /// <summary>Totals a sequence of amounts per currency.</summary>
    public static IReadOnlyList<CurrencyTotal> Of(IEnumerable<Money> amounts) => Of(amounts, m => m);
}
