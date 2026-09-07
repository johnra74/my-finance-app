using MyFinance.Core.Entities;
using MyFinance.Core.Investments;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Investments;

/// <summary>
/// Average-cost holding arithmetic, and what a valuation is allowed to claim.
/// </summary>
public class HoldingCalculatorTests
{
    private static HoldingState Held(decimal units, decimal cost) =>
        new(Quantity.FromDecimal(units), Money.FromDecimal(cost));

    private static SecurityPrice Price(int year, int month, int day, decimal price) =>
        new() { AsOf = new DateOnly(year, month, day), Price = Money.FromDecimal(price) };

    // -- Buying and selling -------------------------------------------------------------

    [Fact]
    public void A_buy_adds_quantity_and_cost()
    {
        HoldingState after = HoldingCalculator.Buy(HoldingState.Empty, Quantity.FromWhole(10), Money.FromDecimal(1000m));

        after.Quantity.ToDecimal().ShouldBe(10m);
        after.CostBasis.ToDecimal().ShouldBe(1000m);
        after.AverageCost.ToDecimal().ShouldBe(100m);
    }

    [Fact]
    public void Two_buys_at_different_prices_average_out()
    {
        HoldingState after = HoldingCalculator.Buy(
            HoldingCalculator.Buy(HoldingState.Empty, Quantity.FromWhole(10), Money.FromDecimal(1000m)),
            Quantity.FromWhole(10),
            Money.FromDecimal(1400m));

        after.Quantity.ToDecimal().ShouldBe(20m);
        after.CostBasis.ToDecimal().ShouldBe(2400m);
        after.AverageCost.ToDecimal().ShouldBe(120m);
    }

    [Fact]
    public void A_sell_removes_a_proportional_share_of_cost()
    {
        // A quarter of the units carries a quarter of the cost. Not the price it sold at —
        // that is a gain, and gains need lots this deliberately does not track.
        SaleResult sale = HoldingCalculator.Sell(Held(100m, 2000m), Quantity.FromWhole(25));

        sale.CostReleased.ToDecimal().ShouldBe(500m);
        sale.Remaining.Quantity.ToDecimal().ShouldBe(75m);
        sale.Remaining.CostBasis.ToDecimal().ShouldBe(1500m);

        // The average cost per unit is unchanged by a sale, which is the whole point.
        sale.Remaining.AverageCost.ToDecimal().ShouldBe(20m);
    }

    [Fact]
    public void A_holding_sold_to_zero_is_kept_at_zero_with_no_cost()
    {
        SaleResult sale = HoldingCalculator.Sell(Held(10m, 1234.57m), Quantity.FromWhole(10));

        sale.Remaining.Quantity.ShouldBe(Quantity.Zero);

        // Exactly zero, not a rounding crumb left against no units at all.
        sale.Remaining.CostBasis.ShouldBe(Money.Zero);
        sale.CostReleased.ToDecimal().ShouldBe(1234.57m);
    }

    [Fact]
    public void Selling_everything_in_pieces_releases_the_whole_cost()
    {
        // The property that catches an apportionment that leaks: however it is broken up,
        // the cost that comes out is the cost that went in.
        HoldingState state = Held(3m, 100m);
        Money released = Money.Zero;

        for (int i = 0; i < 3; i++)
        {
            SaleResult sale = HoldingCalculator.Sell(state, Quantity.FromWhole(1));
            released += sale.CostReleased;
            state = sale.Remaining;
        }

        state.Quantity.ShouldBe(Quantity.Zero);
        state.CostBasis.ShouldBe(Money.Zero);
        released.ToDecimal().ShouldBe(100m);
    }

    [Fact]
    public void A_sale_larger_than_the_holding_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => HoldingCalculator.Sell(Held(5m, 500m), Quantity.FromWhole(6)));
    }

    [Fact]
    public void A_negative_buy_or_sell_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => HoldingCalculator.Buy(HoldingState.Empty, Quantity.FromWhole(-1), Money.Zero));

        Should.Throw<ArgumentOutOfRangeException>(
            () => HoldingCalculator.Sell(Held(5m, 500m), Quantity.FromWhole(-1)));
    }

    [Fact]
    public void An_empty_holding_has_no_average_cost_rather_than_dividing_by_zero()
    {
        HoldingState.Empty.AverageCost.ShouldBe(Money.Zero);
    }

    // -- Valuation ----------------------------------------------------------------------

    [Fact]
    public void A_holding_is_valued_at_the_latest_price_on_or_before_the_date()
    {
        HoldingValue value = HoldingCalculator.Value(
            Held(10m, 1000m),
            [Price(2026, 1, 1, 100m), Price(2026, 3, 1, 130m)],
            new DateOnly(2026, 6, 30));

        value.Amount.ToDecimal().ShouldBe(1300m);
        value.PriceDate.ShouldBe(new DateOnly(2026, 3, 1));
        value.IsAtCost.ShouldBeFalse();
    }

    [Fact]
    public void A_price_after_the_valuation_date_is_not_used()
    {
        // Otherwise every historical net-worth figure would move every time somebody typed
        // this morning's price.
        HoldingValue value = HoldingCalculator.Value(
            Held(10m, 1000m),
            [Price(2026, 1, 1, 100m), Price(2026, 9, 1, 500m)],
            new DateOnly(2026, 3, 1));

        value.Amount.ToDecimal().ShouldBe(1000m);
        value.PriceDate.ShouldBe(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void A_valued_figure_carries_the_date_of_the_price_behind_it()
    {
        // Prices are hand-entered, so a value is only as current as the last one typed. The
        // date is part of the figure, not metadata about it.
        HoldingValue value = HoldingCalculator.Value(
            Held(10m, 1000m),
            [Price(2025, 6, 30, 90m)],
            new DateOnly(2026, 6, 30));

        value.PriceDate.ShouldBe(new DateOnly(2025, 6, 30));
        value.DaysOld(new DateOnly(2026, 6, 30)).ShouldBe(365);
    }

    [Fact]
    public void A_holding_with_no_price_is_valued_at_cost_and_says_so()
    {
        HoldingValue value = HoldingCalculator.Value(Held(10m, 1000m), [], new DateOnly(2026, 6, 30));

        value.Amount.ToDecimal().ShouldBe(1000m);
        value.IsAtCost.ShouldBeTrue();
        value.PriceDate.ShouldBeNull();
        value.DaysOld(new DateOnly(2026, 6, 30)).ShouldBeNull();
    }

    [Fact]
    public void A_holding_with_only_later_prices_is_valued_at_cost()
    {
        HoldingValue value = HoldingCalculator.Value(
            Held(10m, 1000m),
            [Price(2026, 9, 1, 500m)],
            new DateOnly(2026, 3, 1));

        value.IsAtCost.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_holding_is_worth_nothing()
    {
        HoldingValue value = HoldingCalculator.Value(
            HoldingState.Empty,
            [Price(2026, 1, 1, 100m)],
            new DateOnly(2026, 6, 30));

        value.Amount.ShouldBe(Money.Zero);
    }

    [Fact]
    public void A_fractional_holding_values_exactly()
    {
        HoldingValue value = HoldingCalculator.Value(
            Held(12.3456m, 1000m),
            [Price(2026, 1, 1, 100m)],
            new DateOnly(2026, 6, 30));

        value.Amount.ToDecimal().ShouldBe(1234.56m);
    }
}
