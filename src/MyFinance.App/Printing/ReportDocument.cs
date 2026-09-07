using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MyFinance.App.ViewModels.Pages;
using MyFinance.Core.Printing;
using MyFinance.Core.Reporting;

namespace MyFinance.App.Printing;

/// <summary>
/// A report, on paper: the chart and the table, never one without the other.
/// </summary>
/// <remarks>
/// <para>
/// The chart is <b>redrawn from <see cref="ChartGeometry"/> at print resolution</b>, not
/// captured from the screen. A screenshot of the on-screen control is 96 dpi bitmap arriving
/// on a 600 dpi device; since the geometry is already a pure function, redrawing costs a
/// second renderer and no new arithmetic.
/// </para>
/// <para>
/// Every bar carries its own label and figure. On a colour screen the palette does that work —
/// it is validated for contrast and colour-vision deficiency — but a printer may well be
/// monochrome, and colour that has become five shades of grey carries nothing. Direct labels
/// are what make the printed chart readable at all, which is the same rule
/// <c>008-reports-and-dashboard</c> applies on screen: never colour alone.
/// </para>
/// </remarks>
internal static class ReportDocument
{
    private const double BarBandHeight = 26;
    private const double MaximumBarWidth = 420;

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush Faint = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    /// <summary>
    /// A single ink for every bar.
    /// </summary>
    /// <remarks>
    /// Deliberately one colour rather than the screen's categorical palette. On paper the
    /// series are told apart by their labels, which are right beside them; a printed rainbow
    /// would only invite the reader to match slices to a legend that greyscale has flattened.
    /// </remarks>
    private static readonly Brush Bar = new SolidColorBrush(Color.FromRgb(0x2A, 0x4B, 0x7C));

    static ReportDocument()
    {
        Ink.Freeze();
        Faint.Freeze();
        Bar.Freeze();
    }

    public static int PageCount(int rowCount, PageGeometry geometry)
    {
        int perPage = PrintedTable.RowsPerPage(geometry);
        return 1 + Math.Max(1, (rowCount + perPage - 1) / perPage);   // chart sheet, then the table
    }

    public static FixedDocument Build(
        string title,
        string subtitle,
        IReadOnlyList<ReportRowViewModel> rows,
        IReadOnlyList<BarSlice> bars,
        PageGeometry geometry,
        string? warning = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(geometry);

        FixedDocument document = PrintedTable.NewDocument(geometry);
        int total = PageCount(rows.Count, geometry);

        // The chart first, on its own sheet. Composing it above the table would give page one
        // a different row capacity from every other page — a special case with nothing to
        // recommend it beyond saving a sheet of paper.
        document.Pages.Add(PrintedTable.ComposeSheet(
            title, subtitle, Chart(bars, geometry, warning), geometry, number: 1, total));

        ColumnFit fit = ColumnFitter.Fit(ReportColumns.All, geometry.UsableWidth);

        foreach (PageContent page in PrintedTable.BuildPages(
            title, subtitle, fit, [.. rows.Select(Cells)], geometry,
            note: warning, pageOffset: 1, pageTotal: total))
        {
            document.Pages.Add(page);
        }

        return document;
    }

    private static UIElement Chart(IReadOnlyList<BarSlice> bars, PageGeometry geometry, string? warning)
    {
        var panel = new StackPanel();

        if (!string.IsNullOrWhiteSpace(warning))
        {
            // Uncategorized spending makes every percentage on the page suspect. Said above
            // the chart on screen, and said above it here.
            panel.Children.Add(new TextBlock
            {
                Text = warning,
                FontSize = 10,
                Foreground = Ink,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (bars.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Nothing to chart for this selection.",
                FontSize = 11,
                Foreground = Faint,
            });

            return panel;
        }

        double labelWidth = Math.Min(240, geometry.UsableWidth * 0.35);
        double barWidth = Math.Min(MaximumBarWidth, geometry.UsableWidth - labelWidth - 120);

        foreach (BarSlice slice in bars)
        {
            var row = new Grid { Height = BarBandHeight };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = slice.Label,
                FontSize = 9.5,
                Foreground = Ink,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };

            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            // Length from the same fraction the screen uses — ChartGeometry scales against the
            // longest bar, not the total, and that arithmetic is already covered by tests.
            var bar = new Border
            {
                Width = Math.Max(1, barWidth * slice.Fraction),
                Height = 12,
                Background = Bar,
                CornerRadius = new CornerRadius(0, 2, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);

            // The figure, beside the bar. This is what survives a monochrome printer.
            var amount = new TextBlock
            {
                Text = $"{slice.Amount.Abs().ToString("C", CultureInfo.CurrentCulture)}  ({slice.Share:P1})",
                FontSize = 9,
                Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            Grid.SetColumn(amount, 2);
            row.Children.Add(amount);

            panel.Children.Add(row);
        }

        return panel;
    }

    private static Func<string, string> Cells(ReportRowViewModel row) => key => key switch
    {
        "label" => row.Label,
        "amount" => row.AmountText,
        "share" => row.Share.ToString("P1", CultureInfo.CurrentCulture),
        "count" => row.Count.ToString(CultureInfo.CurrentCulture),
        _ => string.Empty,
    };
}
