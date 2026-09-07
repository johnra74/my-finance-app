using System.Globalization;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Reporting;

/// <summary>One bar of a horizontal magnitude chart.</summary>
/// <param name="Key">Grouping key, so a click can drill into it.</param>
/// <param name="Label">Category or payee name.</param>
/// <param name="Amount">The signed figure behind the bar.</param>
/// <param name="Fraction">Length as a share of the longest bar, between zero and one.</param>
/// <param name="Share">Share of the chart's total, for the label.</param>
public sealed record BarSlice(int? Key, string Label, Money Amount, double Fraction, decimal Share)
{
    public string AmountText => Amount.Abs().ToString("C", CultureInfo.CurrentCulture);

    public string ShareText => Share.ToString("P1", CultureInfo.CurrentCulture);
}

/// <summary>A horizontal bar chart, ready to render.</summary>
public sealed record BarChart
{
    public static BarChart Empty { get; } = new() { Title = string.Empty, Bars = [] };

    public required string Title { get; init; }

    public required IReadOnlyList<BarSlice> Bars { get; init; }

    public Money Total { get; init; }

    /// <summary>How many groups were folded into the trailing "Other" bar.</summary>
    public int FoldedCount { get; init; }

    public bool IsEmpty => Bars.Count == 0;
}

/// <summary>One period of a grouped column chart.</summary>
/// <param name="Label">Axis label, e.g. "Mar 2026".</param>
/// <param name="Income">Money in.</param>
/// <param name="Spending">Money out, as a negative figure.</param>
/// <param name="IncomeFraction">Column height as a share of the tallest, zero to one.</param>
/// <param name="SpendingFraction">Same, for the spending column.</param>
public sealed record ColumnPair(
    string Label,
    Money Income,
    Money Spending,
    double IncomeFraction,
    double SpendingFraction)
{
    public Money Net => Income + Spending;

    public string IncomeText => Income.ToString("C", CultureInfo.CurrentCulture);

    public string SpendingText => Spending.Abs().ToString("C", CultureInfo.CurrentCulture);

    public string NetText => Net.ToString("C", CultureInfo.CurrentCulture);
}

/// <summary>A grouped column chart over time.</summary>
public sealed record ColumnChart
{
    public static ColumnChart Empty { get; } = new() { Title = string.Empty, Columns = [], Ticks = [] };

    public required string Title { get; init; }

    public required IReadOnlyList<ColumnPair> Columns { get; init; }

    /// <summary>Gridline values from zero to the top of the scale.</summary>
    public required IReadOnlyList<AxisTick> Ticks { get; init; }

    public bool IsEmpty => Columns.Count == 0;
}

/// <summary>One gridline: what it means and where it sits.</summary>
/// <param name="Value">The figure at this line.</param>
/// <param name="Fraction">Its position up the plot, zero at the baseline.</param>
public sealed record AxisTick(Money Value, double Fraction)
{
    public string Label => Value.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>One plotted point of a line chart, in the logical viewport.</summary>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position, measured downward as the screen does.</param>
/// <param name="Label">Axis label for this point.</param>
/// <param name="Value">The figure it plots.</param>
public sealed record LinePoint(double X, double Y, string Label, Money Value)
{
    public string ValueText => Value.ToString("C", CultureInfo.CurrentCulture);
}

/// <summary>A single-series line chart laid out in a fixed logical viewport.</summary>
/// <remarks>
/// Positions are computed here rather than in the view so the arithmetic is testable —
/// an off-by-one in a baseline or a scale is a chart that lies, and that is exactly the
/// kind of error a screenshot review will not catch.
/// </remarks>
public sealed record LineChart
{
    public static LineChart Empty { get; } =
        new() { Title = string.Empty, Points = [], Ticks = [], Width = 0, Height = 0 };

    public required string Title { get; init; }

    public required IReadOnlyList<LinePoint> Points { get; init; }

    public required IReadOnlyList<AxisTick> Ticks { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    /// <summary>Where zero sits, so a series that crosses it shows the crossing.</summary>
    public double ZeroY { get; init; }

    public bool CrossesZero { get; init; }

    /// <summary>The points as the "x1,y1 x2,y2" string a polyline wants.</summary>
    public string PolylinePoints => string.Join(
        " ",
        Points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.##},{p.Y:0.##}")));

    public bool IsEmpty => Points.Count == 0;
}

/// <summary>
/// Turns report figures into the geometry a chart draws.
/// </summary>
/// <remarks>
/// <para>
/// Pure and separate from any drawing code, for the same reason the report engine is: the
/// numbers have to be right, and a bar whose length disagrees with its label is a lie the
/// eye cannot catch. Everything here is covered by tests that need no window.
/// </para>
/// <para>
/// Colour is deliberately absent. A magnitude bar chart uses one hue for every bar — colouring
/// each bar darker-where-bigger would double-encode the length the bar already shows and burn
/// the only free channel on nothing. The view supplies the single hue.
/// </para>
/// </remarks>
public static class ChartGeometry
{
    /// <summary>
    /// How many bars are drawn before the tail is folded together.
    /// </summary>
    /// <remarks>
    /// Past this the chart stops being readable and the table beneath it is the better
    /// instrument. Folding is honest — the remainder keeps its total — where truncating
    /// would silently drop spending off the chart.
    /// </remarks>
    public const int MaximumBars = 12;

    /// <summary>Builds a horizontal bar chart from a grouped report.</summary>
    public static BarChart Bars(GroupedReport report, int maximumBars = MaximumBars)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Rows.Count == 0)
        {
            return BarChart.Empty with { Title = report.Title };
        }

        List<ReportGroup> kept = [.. report.Rows.Take(maximumBars)];
        List<ReportGroup> folded = [.. report.Rows.Skip(maximumBars)];

        var slices = new List<BarSlice>(kept.Count + 1);

        // Scaled against the longest bar rather than the total, so the smallest bars stay
        // visible instead of collapsing to a hairline.
        long longest = kept.Max(r => Math.Abs(r.Amount.MinorUnits));

        foreach (ReportGroup row in kept)
        {
            slices.Add(new BarSlice(
                row.Key,
                row.Label,
                row.Amount,
                longest == 0 ? 0 : (double)Math.Abs(row.Amount.MinorUnits) / longest,
                row.Share));
        }

        if (folded.Count > 0)
        {
            Money rest = Money.Sum(folded.Select(f => f.Amount));

            slices.Add(new BarSlice(
                null,
                $"Other ({folded.Count} more)",
                rest,
                longest == 0 ? 0 : (double)Math.Abs(rest.MinorUnits) / longest,
                folded.Sum(f => f.Share)));
        }

        return new BarChart
        {
            Title = report.Title,
            Bars = slices,
            Total = report.Total,
            FoldedCount = folded.Count,
        };
    }

    /// <summary>Builds a grouped column chart from a time series.</summary>
    public static ColumnChart Columns(TimeSeriesReport report, int tickCount = 4)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Periods.Count == 0)
        {
            return ColumnChart.Empty with { Title = report.Title };
        }

        long tallest = report.Periods
            .SelectMany(p => new[] { Math.Abs(p.Income.MinorUnits), Math.Abs(p.Spending.MinorUnits) })
            .DefaultIfEmpty(0)
            .Max();

        long scale = NiceCeiling(tallest);

        var columns = new List<ColumnPair>(report.Periods.Count);

        foreach (ReportPeriodTotal period in report.Periods)
        {
            columns.Add(new ColumnPair(
                period.Label,
                period.Income,
                period.Spending,
                scale == 0 ? 0 : (double)Math.Abs(period.Income.MinorUnits) / scale,
                scale == 0 ? 0 : (double)Math.Abs(period.Spending.MinorUnits) / scale));
        }

        return new ColumnChart
        {
            Title = report.Title,
            Columns = columns,
            Ticks = TicksTo(scale, tickCount),
        };
    }

    /// <summary>
    /// Lays a single series out as a line in a logical viewport.
    /// </summary>
    /// <param name="title">Chart title.</param>
    /// <param name="values">The series, in order, with its axis labels.</param>
    /// <param name="width">Logical width of the plot.</param>
    /// <param name="height">Logical height of the plot.</param>
    /// <param name="tickCount">How many gridlines to draw.</param>
    public static LineChart Line(
        string title,
        IReadOnlyList<(string Label, Money Value)> values,
        double width = 900,
        double height = 260,
        int tickCount = 4)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return LineChart.Empty with { Title = title };
        }

        long highest = values.Max(v => v.Value.MinorUnits);
        long lowest = values.Min(v => v.Value.MinorUnits);

        // The scale always includes zero. A net-worth line floating on a scale that starts
        // at its own minimum exaggerates every wobble into a cliff.
        long top = NiceCeiling(Math.Max(highest, 0));
        long bottom = lowest < 0 ? -NiceCeiling(Math.Abs(lowest)) : 0;

        long span = top - bottom;
        if (span == 0)
        {
            span = 1;
        }

        double ToY(long minorUnits) => height - ((double)(minorUnits - bottom) / span * height);

        var points = new List<LinePoint>(values.Count);

        for (int i = 0; i < values.Count; i++)
        {
            // A single point sits in the middle rather than at the left edge, where it would
            // read as the start of a line that is not there.
            double x = values.Count == 1
                ? width / 2
                : (double)i / (values.Count - 1) * width;

            points.Add(new LinePoint(x, ToY(values[i].Value.MinorUnits), values[i].Label, values[i].Value));
        }

        var ticks = new List<AxisTick>(tickCount + 1);

        for (int i = 0; i <= tickCount; i++)
        {
            long value = bottom + (span * i / tickCount);
            ticks.Add(new AxisTick(Money.FromMinorUnits(value), (double)i / tickCount));
        }

        return new LineChart
        {
            Title = title,
            Points = points,
            Ticks = ticks,
            Width = width,
            Height = height,
            ZeroY = ToY(0),
            CrossesZero = bottom < 0 && top > 0,
        };
    }

    /// <summary>Evenly spaced gridlines from zero to a scale top.</summary>
    private static IReadOnlyList<AxisTick> TicksTo(long scale, int tickCount)
    {
        var ticks = new List<AxisTick>(tickCount + 1);

        for (int i = 0; i <= tickCount; i++)
        {
            ticks.Add(new AxisTick(
                Money.FromMinorUnits(scale * i / tickCount),
                (double)i / tickCount));
        }

        return ticks;
    }

    /// <summary>
    /// Rounds a scale top up to a figure a person would choose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An axis topping out at exactly the largest value puts that bar flush against the
    /// frame and gives gridlines at meaningless numbers. Rounding to a round multiple of a
    /// power of ten is what someone drawing the axis by hand would do.
    /// </para>
    /// <para>
    /// The ladder is in tenths rather than the obvious 1-2-5, because a coarse ladder wastes
    /// the plot: a net worth of £101,000 against a scale jumping straight from £100,000 to
    /// £200,000 draws its line across the middle of an otherwise empty chart.
    /// </para>
    /// </remarks>
    internal static long NiceCeiling(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        ReadOnlySpan<int> tenths = [10, 12, 15, 20, 25, 30, 40, 50, 60, 80, 100];

        // The largest power of ten no greater than the value.
        long magnitude = 1;

        while (magnitude <= value / 10)
        {
            magnitude *= 10;
        }

        foreach (int step in tenths)
        {
            long candidate = magnitude * step / 10;

            if (candidate >= value)
            {
                return candidate;
            }
        }

        return magnitude * 10;
    }
}
