using MyFinance.Core.Primitives;
using MyFinance.Core.Reporting;

namespace MyFinance.Core.Tests.Reporting;

public sealed class BarChartTests
{
    [Fact]
    public void Bars_are_scaled_against_the_longest_not_the_total()
    {
        // Scaling against the total would collapse every small bar to a hairline; the point
        // of the chart is that the small ones stay readable next to the big one.
        BarChart chart = ChartGeometry.Bars(Report(
            ("Bills : Rent", -1_000m),
            ("Food : Groceries", -500m),
            ("Food : Coffee", -100m)));

        chart.Bars[0].Fraction.ShouldBe(1.0);
        chart.Bars[1].Fraction.ShouldBe(0.5);
        chart.Bars[2].Fraction.ShouldBe(0.1);
    }

    [Fact]
    public void A_long_tail_is_folded_rather_than_truncated()
    {
        List<(string, decimal)> rows =
        [
            .. Enumerable.Range(1, 20).Select(i => ($"Category {i:00}", -(decimal)(100 - i))),
        ];

        BarChart chart = ChartGeometry.Bars(Report([.. rows]), maximumBars: 5);

        // Truncating would silently drop spending off the chart. Folding keeps the total
        // honest and says how many were folded.
        chart.Bars.Count.ShouldBe(6);
        chart.Bars[^1].Label.ShouldBe("Other (15 more)");
        chart.FoldedCount.ShouldBe(15);

        Money charted = Money.Sum(chart.Bars.Select(b => b.Amount));
        charted.ShouldBe(chart.Total);
    }

    [Fact]
    public void A_short_report_is_not_folded()
    {
        BarChart chart = ChartGeometry.Bars(Report(("Bills : Rent", -1_000m)));

        chart.Bars.Count.ShouldBe(1);
        chart.FoldedCount.ShouldBe(0);
    }

    [Fact]
    public void An_empty_report_produces_an_empty_chart_rather_than_throwing()
    {
        BarChart chart = ChartGeometry.Bars(GroupedReport.Empty);

        chart.IsEmpty.ShouldBeTrue();
        chart.Bars.ShouldBeEmpty();
    }

    [Fact]
    public void A_report_of_zeroes_does_not_divide_by_zero()
    {
        BarChart chart = ChartGeometry.Bars(Report(("Food : Coffee", 0m)));

        chart.Bars[0].Fraction.ShouldBe(0);
    }

    [Fact]
    public void Each_bar_keeps_its_key_so_a_click_can_drill_into_it()
    {
        BarChart chart = ChartGeometry.Bars(Report(("Food : Coffee", -20m)));

        chart.Bars[0].Key.ShouldNotBeNull();

        // The folded bar has no single key, because it stands for many categories.
        BarChart folded = ChartGeometry.Bars(
            Report(("A", -30m), ("B", -20m), ("C", -10m)),
            maximumBars: 1);

        folded.Bars[^1].Key.ShouldBeNull();
    }

    private static GroupedReport Report(params (string Label, decimal Amount)[] rows)
    {
        Money total = Money.Sum(rows.Select(r => Money.FromDecimal(r.Amount)));

        return new GroupedReport
        {
            Title = "Spending by category",
            Total = total,
            EntryCount = rows.Length,
            Rows =
            [
                .. rows.Select((r, i) => new ReportGroup(
                    i + 1,
                    r.Label,
                    Money.FromDecimal(r.Amount),
                    1)
                {
                    Share = total.IsZero ? 0 : (decimal)Math.Abs(Money.FromDecimal(r.Amount).MinorUnits)
                        / Math.Abs(total.MinorUnits),
                }),
            ],
        };
    }
}

public sealed class ColumnChartTests
{
    [Fact]
    public void Both_series_share_one_scale()
    {
        // Two y-scales on one plot invent a correlation that is not in the data. Income and
        // spending are the same measure, so they share the axis.
        ColumnChart chart = ChartGeometry.Columns(Series(
            ("Jan 2026", 3_000m, -1_500m),
            ("Feb 2026", 3_000m, -3_000m)));

        chart.Columns[0].IncomeFraction.ShouldBe(chart.Columns[1].IncomeFraction);
        chart.Columns[1].SpendingFraction.ShouldBe(chart.Columns[1].IncomeFraction);
        chart.Columns[0].SpendingFraction.ShouldBe(chart.Columns[0].IncomeFraction / 2);
    }

    [Fact]
    public void The_axis_tops_out_at_a_round_number()
    {
        ColumnChart chart = ChartGeometry.Columns(Series(("Jan 2026", 3_142m, -1_000m)));

        // A bar flush against the frame with gridlines at meaningless numbers is what an
        // un-rounded axis produces — but the ladder is fine enough not to waste the plot.
        chart.Ticks[^1].Value.ShouldBe(Money.FromDecimal(4_000m));
        chart.Ticks[0].Value.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Ticks_are_evenly_spaced_from_the_baseline()
    {
        ColumnChart chart = ChartGeometry.Columns(Series(("Jan 2026", 1_000m, 0m)), tickCount: 4);

        chart.Ticks.Count.ShouldBe(5);
        chart.Ticks.Select(t => t.Fraction).ShouldBe([0, 0.25, 0.5, 0.75, 1.0]);
    }

    [Fact]
    public void An_empty_series_produces_an_empty_chart()
    {
        ChartGeometry.Columns(TimeSeriesReport.Empty).IsEmpty.ShouldBeTrue();
    }

    private static TimeSeriesReport Series(params (string Label, decimal In, decimal Out)[] periods) =>
        new()
        {
            Title = "Income and spending over time",
            Periods =
            [
                .. periods.Select((p, i) => new ReportPeriodTotal(
                    new DateOnly(2026, i + 1, 1),
                    p.Label,
                    Money.FromDecimal(p.In),
                    Money.FromDecimal(p.Out))),
            ],
        };
}

public sealed class LineChartTests
{
    [Fact]
    public void Points_run_left_to_right_across_the_viewport()
    {
        LineChart chart = ChartGeometry.Line(
            "Net worth",
            [("Jan", Money.FromDecimal(1_000m)), ("Feb", Money.FromDecimal(2_000m)), ("Mar", Money.FromDecimal(3_000m))],
            width: 900,
            height: 260);

        chart.Points[0].X.ShouldBe(0);
        chart.Points[1].X.ShouldBe(450);
        chart.Points[2].X.ShouldBe(900);
    }

    [Fact]
    public void Higher_values_sit_higher_on_the_screen()
    {
        LineChart chart = ChartGeometry.Line(
            "Net worth",
            [("Jan", Money.FromDecimal(1_000m)), ("Feb", Money.FromDecimal(3_000m))]);

        // Screen coordinates run downward, so a bigger figure is a smaller Y.
        chart.Points[1].Y.ShouldBeLessThan(chart.Points[0].Y);
    }

    [Fact]
    public void The_scale_always_includes_zero()
    {
        LineChart chart = ChartGeometry.Line(
            "Net worth",
            [("Jan", Money.FromDecimal(100_000m)), ("Feb", Money.FromDecimal(101_000m))],
            height: 100);

        // A line floating on a scale that starts at its own minimum turns a one-percent
        // wobble into a cliff. Both points sit near the top of a scale rooted at zero.
        chart.Points.ShouldAllBe(p => p.Y < 20);
        chart.Ticks[0].Value.ShouldBe(Money.Zero);
    }

    [Fact]
    public void A_series_that_goes_negative_gets_a_scale_below_zero()
    {
        LineChart chart = ChartGeometry.Line(
            "Net worth",
            [("Jan", Money.FromDecimal(1_000m)), ("Feb", Money.FromDecimal(-500m))],
            height: 300);

        chart.CrossesZero.ShouldBeTrue();
        chart.ZeroY.ShouldBeGreaterThan(0);
        chart.ZeroY.ShouldBeLessThan(300);
        chart.Ticks[0].Value.MinorUnits.ShouldBeLessThan(0);
    }

    [Fact]
    public void A_single_point_sits_in_the_middle()
    {
        LineChart chart = ChartGeometry.Line(
            "Net worth",
            [("Jan", Money.FromDecimal(1_000m))],
            width: 900);

        // At the left edge it would read as the start of a line that is not there.
        chart.Points.Single().X.ShouldBe(450);
    }

    [Fact]
    public void The_polyline_string_is_culture_independent()
    {
        LineChart chart = ChartGeometry.Line(
            "Net worth",
            [("Jan", Money.FromDecimal(1_000m)), ("Feb", Money.FromDecimal(2_000m))],
            width: 100,
            height: 100);

        // A comma decimal separator would turn "50,5" into two coordinates and corrupt the
        // shape on any machine with a European locale.
        chart.PolylinePoints.ShouldNotContain(";");
        chart.PolylinePoints.Split(' ').Length.ShouldBe(2);
        chart.PolylinePoints.Split(' ')[0].Split(',').Length.ShouldBe(2);
    }

    [Fact]
    public void An_empty_series_produces_an_empty_chart()
    {
        ChartGeometry.Line("Net worth", []).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_flat_series_does_not_divide_by_zero()
    {
        LineChart chart = ChartGeometry.Line(
            "Net worth",
            [("Jan", Money.Zero), ("Feb", Money.Zero)]);

        chart.Points.Count.ShouldBe(2);
        double.IsNaN(chart.Points[0].Y).ShouldBeFalse();
    }
}

public sealed class NiceCeilingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(7, 8)]
    [InlineData(12, 12)]
    [InlineData(31, 40)]
    [InlineData(99, 100)]
    [InlineData(314, 400)]
    [InlineData(1_000, 1_000)]
    [InlineData(1_001, 1_200)]
    public void A_scale_top_rounds_to_a_figure_a_person_would_choose(long value, long expected) =>
        ChartGeometry.NiceCeiling(value).ShouldBe(expected);
}
