using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MyFinance.Core.Printing;

namespace MyFinance.App.Printing;

/// <summary>
/// Builds a paginated, printable table.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="FixedDocument"/> rather than a <see cref="FlowDocument"/>, because the
/// pagination is already decided: <see cref="PageLayout"/> works out how many rows fit and
/// where the breaks fall, and this composes one page per result. Handing a flow document to
/// WPF and letting it break where it likes would move every checkable property — rows per
/// page, repeated headings, no row lost or repeated — into the one layer this repository
/// cannot verify.
/// </para>
/// <para>
/// Everything drawn here is text the view model already formatted. There is no second
/// formatting path that could disagree with the screen.
/// </para>
/// </remarks>
internal static class PrintedTable
{
    private const double RowHeight = 20;
    private const double HeaderBandHeight = 26;
    private const double FooterHeight = 28;
    private const double TitleHeight = 54;

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush Faint = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly Brush Rule = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));

    static PrintedTable()
    {
        Ink.Freeze();
        Faint.Freeze();
        Rule.Freeze();
    }

    /// <summary>How many rows fit a page of this geometry, given the title and footer bands.</summary>
    public static int RowsPerPage(PageGeometry geometry) =>
        PageLayout.RowsPerPage(geometry, RowHeight, TitleHeight + HeaderBandHeight, FooterHeight);

    /// <summary>
    /// Composes the document.
    /// </summary>
    /// <param name="title">Names what this is — the account, the report.</param>
    /// <param name="subtitle">Names the period, and the filter if one is in force.</param>
    /// <param name="fit">Which columns print, from <see cref="ColumnFitter"/>.</param>
    /// <param name="rows">Already-formatted cells: one lookup per row, keyed by column.</param>
    /// <param name="note">Shown under the title — e.g. that columns were left out.</param>
    public static FixedDocument Build(
        string title,
        string subtitle,
        ColumnFit fit,
        IReadOnlyList<Func<string, string>> rows,
        PageGeometry geometry,
        string? note = null)
    {
        var document = NewDocument(geometry);

        foreach (PageContent page in BuildPages(title, subtitle, fit, rows, geometry, note))
        {
            document.Pages.Add(page);
        }

        return document;
    }

    public static FixedDocument NewDocument(PageGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(geometry.Width, geometry.Height);
        return document;
    }

    /// <summary>
    /// The table's pages, optionally numbered as part of a larger document.
    /// </summary>
    /// <param name="pageOffset">How many pages come before these.</param>
    /// <param name="pageTotal">The whole document's page count, or null to use just these.</param>
    public static IReadOnlyList<PageContent> BuildPages(
        string title,
        string subtitle,
        ColumnFit fit,
        IReadOnlyList<Func<string, string>> rows,
        PageGeometry geometry,
        string? note = null,
        int pageOffset = 0,
        int? pageTotal = null)
    {
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(geometry);

        IReadOnlyList<PrintedPage<Func<string, string>>> pages =
            PageLayout.Paginate(rows, RowsPerPage(geometry));

        int total = pageTotal ?? pages.Count;

        return
        [
            .. pages.Select(p => Compose(
                title,
                subtitle,
                note,
                fit,
                new PrintedPage<Func<string, string>>(p.Number + pageOffset, total, p.Rows),
                geometry))
        ];
    }

    /// <summary>Composes a page that is not a table — the chart sheet, for instance.</summary>
    public static PageContent ComposeSheet(
        string title,
        string subtitle,
        UIElement content,
        PageGeometry geometry,
        int number,
        int total)
    {
        var body = new StackPanel { Margin = new Thickness(geometry.Margin) };

        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
        });

        body.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 10,
            Foreground = Faint,
            Margin = new Thickness(0, 2, 0, 14),
        });

        body.Children.Add(content);

        return Wrap(body, geometry, new PrintedPage<int>(number, total, []));
    }

    private static PageContent Compose(
        string title,
        string subtitle,
        string? note,
        ColumnFit fit,
        PrintedPage<Func<string, string>> page,
        PageGeometry geometry)
    {
        var body = new StackPanel { Margin = new Thickness(geometry.Margin) };

        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
        });

        body.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 10,
            Foreground = Faint,
            Margin = new Thickness(0, 2, 0, 0),
        });

        if (!string.IsNullOrWhiteSpace(note))
        {
            // Said on every page, because the reader of page 7 did not necessarily see page 1.
            body.Children.Add(new TextBlock
            {
                Text = note,
                FontSize = 9,
                Foreground = Faint,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        // The heading band, drawn above every page's rows by construction — there is no
        // "first page only" path here to get wrong.
        body.Children.Add(HeaderBand(fit));

        if (page.IsEmpty)
        {
            body.Children.Add(new TextBlock
            {
                Text = "Nothing to print for this selection.",
                FontSize = 11,
                Foreground = Faint,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }
        else
        {
            foreach (Func<string, string> row in page.Rows)
            {
                body.Children.Add(RowBand(fit, row));
            }
        }

        return Wrap(body, geometry, page);
    }

    private static PageContent Wrap<T>(UIElement body, PageGeometry geometry, PrintedPage<T> page)
    {
        var canvas = new Canvas
        {
            Width = geometry.Width,
            Height = geometry.Height,
            Background = Brushes.White,
        };

        canvas.Children.Add(body);

        var footer = new TextBlock
        {
            Text = page.NumberText,
            FontSize = 9,
            Foreground = Faint,
            Width = geometry.UsableWidth,
            TextAlignment = TextAlignment.Right,
        };

        Canvas.SetLeft(footer, geometry.Margin);
        Canvas.SetTop(footer, geometry.Height - geometry.Margin);
        canvas.Children.Add(footer);

        var fixedPage = new FixedPage
        {
            Width = geometry.Width,
            Height = geometry.Height,
        };

        fixedPage.Children.Add(canvas);

        var content = new PageContent();
        ((IAddChild)content).AddChild(fixedPage);
        return content;
    }

    private static UIElement HeaderBand(ColumnFit fit)
    {
        var grid = Row(fit);
        grid.Margin = new Thickness(0, 10, 0, 0);

        int i = 0;

        foreach (PrintColumn column in fit.Columns)
        {
            var cell = new TextBlock
            {
                Text = column.Header,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink,
                TextAlignment = column.IsNumeric ? TextAlignment.Right : TextAlignment.Left,
                Margin = new Thickness(0, 0, 6, 3),
            };

            Grid.SetColumn(cell, i++);
            grid.Children.Add(cell);
        }

        var band = new StackPanel();
        band.Children.Add(grid);
        band.Children.Add(new Border { Height = 1, Background = Rule });
        return band;
    }

    private static UIElement RowBand(ColumnFit fit, Func<string, string> row)
    {
        Grid grid = Row(fit);
        grid.Height = RowHeight;

        int i = 0;

        foreach (PrintColumn column in fit.Columns)
        {
            var cell = new TextBlock
            {
                Text = row(column.Key),
                FontSize = 9.5,
                Foreground = Ink,
                TextAlignment = column.IsNumeric ? TextAlignment.Right : TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 3, 6, 0),
            };

            Grid.SetColumn(cell, i++);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private static Grid Row(ColumnFit fit)
    {
        var grid = new Grid();

        foreach (PrintColumn column in fit.Columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(column.Width, GridUnitType.Star),
            });
        }

        return grid;
    }

    /// <summary>Reads "1 January 2026 to 31 March 2026", or "everything" when unbounded.</summary>
    public static string PeriodText(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        (null, null) => "All dates",
        (DateOnly f, null) => $"From {f.ToString("d", CultureInfo.CurrentCulture)}",
        (null, DateOnly t) => $"To {t.ToString("d", CultureInfo.CurrentCulture)}",
        (DateOnly f, DateOnly t) =>
            $"{f.ToString("d", CultureInfo.CurrentCulture)} to {t.ToString("d", CultureInfo.CurrentCulture)}",
    };
}
