using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Investments;

/// <summary>What is held of one security, and what it cost.</summary>
public readonly record struct HoldingState(Quantity Quantity, Money CostBasis)
{
    public static HoldingState Empty => new(Quantity.Zero, Money.Zero);

    /// <summary>Cost of one unit. Zero when nothing is held.</summary>
    public Money AverageCost => Quantity.IsZero
        ? Money.Zero
        : CostBasis * (1m / Quantity.ToDecimal());
}

/// <summary>The result of a sale: what is left, and what the sold units had cost.</summary>
public readonly record struct SaleResult(HoldingState Remaining, Money CostReleased);

/// <summary>
/// What a holding is worth, and on what evidence.
/// </summary>
/// <param name="Amount">The value.</param>
/// <param name="PriceDate">
/// The date of the price used. Null when there was none and the holding is carried at cost.
/// </param>
/// <param name="IsAtCost">
/// True when no price was available. The figure is what was paid, not what it is worth, and
/// anything showing it has to say so.
/// </param>
public sealed record HoldingValue(Money Amount, DateOnly? PriceDate, bool IsAtCost)
{
    /// <summary>How stale the price is, as at a date. Null when carried at cost.</summary>
    public int? DaysOld(DateOnly asOf) =>
        PriceDate is DateOnly date ? asOf.DayNumber - date.DayNumber : null;
}

/// <summary>
/// One thing that moved a holding, flattened for replay.
/// </summary>
/// <param name="Cost">What the units cost, for a purchase. Ignored on a sale.</param>
public sealed record InvestmentMovement(
    int AccountId,
    int SecurityId,
    DateOnly Date,
    bool IsPurchase,
    Quantity Quantity,
    Money Cost);

/// <summary>
/// Average-cost holding arithmetic, and honest valuation.
/// </summary>
/// <remarks>
/// <para>
/// A buy adds units and cost. A sell removes units and a <em>proportional</em> share of the
/// cost — the average-cost method. It is exact and enough to answer "what do I hold" and "what
/// did I pay"; it is not enough for capital gains, which need individual lots and a matching
/// policy the user would have to choose per sale. This application has no tax report, so lots
/// would be machinery in service of a feature that does not exist.
/// </para>
/// <para>
/// Valuation is deliberately careful about its own evidence. Prices are entered by hand —
/// constitution 7 rules out fetching them — so a value is only as current as the last time
/// somebody typed one, and every figure carries the date of the price behind it. A holding
/// with no price at all is valued at cost and says so, rather than silently reading as though
/// it were worth what was paid.
/// </para>
/// </remarks>
public static class HoldingCalculator
{
    /// <summary>Adds units at a cost.</summary>
    public static HoldingState Buy(HoldingState current, Quantity units, Money cost)
    {
        if (units.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(units), "A purchase cannot be negative.");
        }

        return new HoldingState(current.Quantity + units, current.CostBasis + cost);
    }

    /// <summary>
    /// Removes units and the share of the cost they carried.
    /// </summary>
    /// <remarks>
    /// Selling everything releases the whole cost basis exactly, leaving zero rather than a
    /// rounding crumb — which is why the last sale is special-cased rather than apportioned.
    /// </remarks>
    public static SaleResult Sell(HoldingState current, Quantity units)
    {
        if (units.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(units), "A sale cannot be negative.");
        }

        if (units > current.Quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(units),
                $"Cannot sell {units} units of a holding of {current.Quantity}.");
        }

        if (units == current.Quantity)
        {
            // The whole thing. Releasing the basis exactly avoids leaving a cent behind
            // against a quantity of zero, which would then have no units to belong to.
            return new SaleResult(
                new HoldingState(Quantity.Zero, Money.Zero),
                current.CostBasis);
        }

        Money released = current.CostBasis * units.FractionOf(current.Quantity);

        return new SaleResult(
            new HoldingState(current.Quantity - units, current.CostBasis - released),
            released);
    }

    /// <summary>
    /// Replays activity to work out what was held on a date.
    /// </summary>
    /// <remarks>
    /// Net worth in June 2019 must reflect what was held in June 2019, not what is held today.
    /// Valuing a historical point from the current holding would show shares as owned years
    /// before they were bought — and the error grows the further back the chart goes, which is
    /// exactly where somebody looks to see how they have done.
    /// </remarks>
    public static IReadOnlyDictionary<(int AccountId, int SecurityId), HoldingState> StateAt(
        IEnumerable<InvestmentMovement> movements,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(movements);

        var states = new Dictionary<(int, int), HoldingState>();

        foreach (InvestmentMovement move in movements
                     .Where(m => m.Date <= asOf)
                     .OrderBy(m => m.Date))
        {
            (int, int) key = (move.AccountId, move.SecurityId);
            HoldingState state = states.GetValueOrDefault(key, HoldingState.Empty);

            states[key] = move.IsPurchase
                ? Buy(state, move.Quantity, move.Cost)

                // Clamped rather than refused: a replay is reconstructing history, and a
                // corrected or out-of-order record should not stop a chart being drawn.
                : Sell(state, Min(move.Quantity, state.Quantity)).Remaining;
        }

        return states;
    }

    private static Quantity Min(Quantity left, Quantity right) => left < right ? left : right;

    /// <summary>
    /// Values a holding using the most recent price on or before a date.
    /// </summary>
    /// <remarks>
    /// A price dated after <paramref name="asOf"/> is not used: a valuation as at the end of
    /// last year must not be computed from a price set this morning, or every historical net
    /// worth figure would change every time somebody typed a price.
    /// </remarks>
    public static HoldingValue Value(
        HoldingState holding,
        IEnumerable<SecurityPrice> prices,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(prices);

        SecurityPrice? latest = prices
            .Where(p => p.AsOf <= asOf)
            .OrderByDescending(p => p.AsOf)
            .FirstOrDefault();

        if (latest is null)
        {
            return new HoldingValue(holding.CostBasis, PriceDate: null, IsAtCost: true);
        }

        return new HoldingValue(
            latest.Price * holding.Quantity.ToDecimal(),
            latest.AsOf,
            IsAtCost: false);
    }
}
