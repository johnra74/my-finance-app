using MyFinance.Core.Printing;

namespace MyFinance.Core.Tests.Printing;

/// <summary>
/// Pagination arithmetic — the part of printing this repository can actually verify.
/// </summary>
/// <remarks>
/// Everything checkable about a printout is computed here rather than left to WPF, because
/// the alternative is putting it in the one layer no test can reach. What is left for the
/// view is drawing.
/// </remarks>
public class PageLayoutTests
{
    private static IReadOnlyList<int> Rows(int count) => [.. Enumerable.Range(1, count)];

    [Fact]
    public void Rows_fill_a_page_and_the_remainder_starts_the_next()
    {
        IReadOnlyList<PrintedPage<int>> pages = PageLayout.Paginate(Rows(25), rowsPerPage: 10);

        pages.Count.ShouldBe(3);
        pages[0].Rows.Count.ShouldBe(10);
        pages[1].Rows.Count.ShouldBe(10);
        pages[2].Rows.Count.ShouldBe(5);
    }

    [Fact]
    public void A_single_short_page_is_not_split()
    {
        IReadOnlyList<PrintedPage<int>> pages = PageLayout.Paginate(Rows(4), rowsPerPage: 10);

        pages.Count.ShouldBe(1);
        pages[0].Rows.Count.ShouldBe(4);
        pages[0].NumberText.ShouldBe("Page 1 of 1");
    }

    [Fact]
    public void An_exactly_full_page_does_not_produce_an_empty_one_after_it()
    {
        // The off-by-one that shows up as a blank final sheet coming out of the printer.
        IReadOnlyList<PrintedPage<int>> pages = PageLayout.Paginate(Rows(20), rowsPerPage: 10);

        pages.Count.ShouldBe(2);
        pages.ShouldAllBe(p => p.Rows.Count == 10);
    }

    [Fact]
    public void Pages_are_numbered_with_their_total()
    {
        IReadOnlyList<PrintedPage<int>> pages = PageLayout.Paginate(Rows(25), rowsPerPage: 10);

        pages.Select(p => p.Number).ShouldBe([1, 2, 3]);
        pages.ShouldAllBe(p => p.Total == 3);
        pages[1].NumberText.ShouldBe("Page 2 of 3");
    }

    [Fact]
    public void An_empty_result_produces_one_page_saying_so()
    {
        // Not zero pages: a print job that produces no paper leaves the user wondering
        // whether it worked.
        IReadOnlyList<PrintedPage<int>> pages = PageLayout.Paginate(Rows(0), rowsPerPage: 10);

        pages.Count.ShouldBe(1);
        pages[0].IsEmpty.ShouldBeTrue();
        pages[0].NumberText.ShouldBe("Page 1 of 1");
    }

    [Fact]
    public void Every_row_given_appears_on_exactly_one_page()
    {
        // The property worth having. A pagination bug that drops or repeats a row produces a
        // printout nobody can tell is wrong by looking at it.
        IReadOnlyList<int> rows = Rows(97);

        IReadOnlyList<PrintedPage<int>> pages = PageLayout.Paginate(rows, rowsPerPage: 13);

        List<int> printed = [.. pages.SelectMany(p => p.Rows)];

        printed.Count.ShouldBe(rows.Count);
        printed.ShouldBe(rows);                       // same rows, same order
        printed.Distinct().Count().ShouldBe(rows.Count);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 50)]
    [InlineData(7, 3)]
    [InlineData(100, 7)]
    [InlineData(999, 1)]
    public void No_row_is_lost_or_duplicated_at_any_page_size(int rowCount, int perPage)
    {
        IReadOnlyList<int> rows = Rows(rowCount);

        List<int> printed = [.. PageLayout.Paginate(rows, perPage).SelectMany(p => p.Rows)];

        printed.ShouldBe(rows);
    }

    [Fact]
    public void Rows_per_page_comes_from_the_space_left_after_the_heading_and_footer()
    {
        // Letter portrait, half-inch margins: 1056 - 96 = 960 usable.
        PageGeometry page = PageGeometry.LetterPortrait;
        page.UsableHeight.ShouldBe(960);

        PageLayout.RowsPerPage(page, rowHeight: 20).ShouldBe(48);
        PageLayout.RowsPerPage(page, rowHeight: 20, headerHeight: 80, footerHeight: 40).ShouldBe(42);
    }

    [Fact]
    public void A_page_too_small_for_even_one_row_still_takes_one()
    {
        // Zero rows per page would mean an infinite document or an empty print job.
        PageLayout.RowsPerPage(new PageGeometry(200, 120, Margin: 48), rowHeight: 100).ShouldBe(1);
    }

    [Fact]
    public void Landscape_holds_fewer_rows_than_portrait()
    {
        PageLayout.RowsPerPage(PageGeometry.LetterLandscape, 20)
            .ShouldBeLessThan(PageLayout.RowsPerPage(PageGeometry.LetterPortrait, 20));
    }

    [Fact]
    public void A_nonsensical_row_height_is_refused_rather_than_producing_no_pages()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => PageLayout.RowsPerPage(PageGeometry.LetterPortrait, rowHeight: 0));

        Should.Throw<ArgumentOutOfRangeException>(
            () => PageLayout.Paginate(Rows(5), rowsPerPage: 0));
    }
}
