namespace MyFinance.Core.Printing;

/// <summary>A printable page's geometry, in device-independent pixels (1/96 inch).</summary>
/// <remarks>
/// The defaults are US Letter with half-inch margins, which is what this application's one
/// user prints on. A real print dialog reports the actual printable area and it is passed
/// straight through — these exist so the arithmetic can be exercised without a printer.
/// </remarks>
public sealed record PageGeometry(double Width, double Height, double Margin = 48)
{
    public static PageGeometry LetterPortrait { get; } = new(816, 1056);

    public static PageGeometry LetterLandscape { get; } = new(1056, 816);

    public double UsableWidth => Math.Max(0, Width - (Margin * 2));

    public double UsableHeight => Math.Max(0, Height - (Margin * 2));
}

/// <summary>One page of a printed table.</summary>
/// <param name="Number">One-based.</param>
/// <param name="Total">How many pages there are altogether.</param>
/// <param name="Rows">The rows on this page. Empty only when there were none at all.</param>
public sealed record PrintedPage<T>(int Number, int Total, IReadOnlyList<T> Rows)
{
    /// <summary>Reads as "Page 2 of 7".</summary>
    public string NumberText => $"Page {Number} of {Total}";

    /// <summary>
    /// True when there was nothing to print. The page still exists and still says so, rather
    /// than the print job producing no paper and leaving the user wondering.
    /// </summary>
    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>
/// Breaks a table into pages.
/// </summary>
/// <remarks>
/// <para>
/// Computed here rather than handed to WPF's own pagination, because everything checkable
/// about a printout — how many rows fit, where the breaks fall, that the headings repeat, that
/// no row is lost or printed twice — would otherwise move into the one layer this repository
/// cannot verify. What is left for the view is drawing.
/// </para>
/// <para>
/// Column headings repeat on every page by construction: a page carries only its rows, and the
/// document draws the heading band above each one. There is no "first page only" path to get
/// wrong.
/// </para>
/// </remarks>
public static class PageLayout
{
    /// <summary>How many rows fit below the heading band and above the footer.</summary>
    /// <param name="geometry">The page.</param>
    /// <param name="rowHeight">Height of one row.</param>
    /// <param name="headerHeight">The title block, printed once at the top of every page.</param>
    /// <param name="footerHeight">The page number band.</param>
    public static int RowsPerPage(
        PageGeometry geometry,
        double rowHeight,
        double headerHeight = 0,
        double footerHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowHeight);

        double available = geometry.UsableHeight - headerHeight - footerHeight;

        // At least one row per page, even on a page too small to hold it: a page count of
        // zero would mean an infinite loop or a silently empty print job.
        return Math.Max(1, (int)Math.Floor(available / rowHeight));
    }

    /// <summary>
    /// Splits rows into pages.
    /// </summary>
    /// <remarks>
    /// Every row given appears on exactly one page. That is the property worth having: a
    /// pagination bug that drops or repeats a row produces a printout nobody can tell is
    /// wrong by looking at it.
    /// </remarks>
    public static IReadOnlyList<PrintedPage<T>> Paginate<T>(IReadOnlyList<T> rows, int rowsPerPage)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowsPerPage);

        if (rows.Count == 0)
        {
            return [new PrintedPage<T>(1, 1, [])];
        }

        int total = (rows.Count + rowsPerPage - 1) / rowsPerPage;
        var pages = new List<PrintedPage<T>>(total);

        for (int i = 0; i < total; i++)
        {
            pages.Add(new PrintedPage<T>(
                i + 1,
                total,
                [.. rows.Skip(i * rowsPerPage).Take(rowsPerPage)]));
        }

        return pages;
    }

    /// <summary>Convenience: work the rows per page out from the geometry, then paginate.</summary>
    public static IReadOnlyList<PrintedPage<T>> Paginate<T>(
        IReadOnlyList<T> rows,
        PageGeometry geometry,
        double rowHeight,
        double headerHeight = 0,
        double footerHeight = 0) =>
        Paginate(rows, RowsPerPage(geometry, rowHeight, headerHeight, footerHeight));
}
