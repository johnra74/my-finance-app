using MyFinance.Core.Printing;

namespace MyFinance.Core.Tests.Printing;

/// <summary>
/// Which columns a printed page carries, and the rule that they are never dropped silently.
/// </summary>
public class ColumnFitterTests
{
    private static IReadOnlyList<PrintColumn> All => RegisterColumns.All;

    [Fact]
    public void The_default_columns_fit_a_portrait_page()
    {
        // Letter portrait, half-inch margins: 816 - 96 = 720 usable.
        PageGeometry page = PageGeometry.LetterPortrait;
        page.UsableWidth.ShouldBe(720);

        ColumnFit fit = ColumnFitter.Fit(All, page.UsableWidth);

        fit.TotalWidth.ShouldBeLessThanOrEqualTo(720);
        fit.IsCramped.ShouldBeFalse();
    }

    [Fact]
    public void The_columns_a_register_is_printed_for_always_survive()
    {
        // Date, what left, what arrived, the running balance. Losing any of these makes the
        // printout pointless, whatever else has to go.
        ColumnFit fit = ColumnFitter.Fit(All, PageGeometry.LetterPortrait.UsableWidth);

        foreach (string key in new[] { "date", "payment", "deposit", "balance" })
        {
            fit.Columns.ShouldContain(c => c.Key == key);
        }
    }

    [Fact]
    public void A_wider_page_admits_more_columns()
    {
        ColumnFit portrait = ColumnFitter.Fit(All, PageGeometry.LetterPortrait.UsableWidth);
        ColumnFit landscape = ColumnFitter.Fit(All, PageGeometry.LetterLandscape.UsableWidth);

        landscape.Columns.Count.ShouldBeGreaterThan(portrait.Columns.Count);
        landscape.Dropped.Count.ShouldBeLessThan(portrait.Dropped.Count);
    }

    [Fact]
    public void Everything_fits_on_a_page_wide_enough_for_all_of_it()
    {
        ColumnFit fit = ColumnFitter.Fit(All, availableWidth: 5000);

        fit.Columns.Count.ShouldBe(All.Count);
        fit.Dropped.ShouldBeEmpty();
        fit.IsCramped.ShouldBeFalse();
    }

    [Fact]
    public void The_memo_is_the_first_thing_to_go()
    {
        // The widest column, and the one whose absence is most obvious — which is exactly why
        // it must never disappear without having been asked to.
        ColumnFit fit = ColumnFitter.Fit(All, PageGeometry.LetterPortrait.UsableWidth);

        fit.Dropped.ShouldContain(c => c.Key == "memo");
    }

    [Fact]
    public void A_column_the_user_chose_is_never_dropped_to_make_room()
    {
        // The page may be cramped. It may not disagree with what was asked for: a memo column
        // that vanished on its own is not something the reader can notice.
        string[] chosen = ["date", "payee", "memo", "category", "payment", "deposit", "balance"];

        ColumnFit fit = ColumnFitter.Fit(All, availableWidth: 300, chosen);

        foreach (string key in chosen)
        {
            fit.Columns.ShouldContain(c => c.Key == key);
        }

        fit.TotalWidth.ShouldBeGreaterThan(300);
        fit.IsCramped.ShouldBeTrue("the caller has to be able to say the page is over-full");
    }

    [Fact]
    public void A_chosen_set_that_fits_is_not_reported_as_cramped()
    {
        ColumnFit fit = ColumnFitter.Fit(All, availableWidth: 720, ["date", "payee", "balance"]);

        fit.Columns.Select(c => c.Key).ShouldBe(["date", "payee", "balance"]);
        fit.IsCramped.ShouldBeFalse();
    }

    [Fact]
    public void Columns_print_in_the_registers_own_order_however_they_were_chosen()
    {
        // Asked for backwards; must still print the way the register reads.
        ColumnFit fit = ColumnFitter.Fit(All, 720, ["balance", "date", "payee"]);

        fit.Columns.Select(c => c.Key).ShouldBe(["date", "payee", "balance"]);
    }

    [Fact]
    public void What_was_left_out_is_always_reported()
    {
        ColumnFit fit = ColumnFitter.Fit(All, 720, ["date", "balance"]);

        fit.Dropped.Select(c => c.Key).ShouldNotContain("date");
        fit.Dropped.Count.ShouldBe(All.Count - 2);
    }

    [Fact]
    public void At_least_one_column_survives_the_narrowest_page()
    {
        ColumnFit fit = ColumnFitter.Fit(All, availableWidth: 1);

        fit.Columns.Count.ShouldBe(1);
        fit.IsCramped.ShouldBeTrue();
    }

    [Fact]
    public void No_columns_at_all_is_answered_with_nothing_rather_than_a_throw()
    {
        ColumnFit fit = ColumnFitter.Fit([], 720);

        fit.Columns.ShouldBeEmpty();
        fit.IsCramped.ShouldBeFalse();
    }

    [Fact]
    public void Every_register_column_has_a_distinct_key_and_priority()
    {
        // The key is remembered between sessions and the priority decides what goes first;
        // a duplicate in either makes the choice arbitrary.
        All.Select(c => c.Key).Distinct().Count().ShouldBe(All.Count);
        All.Select(c => c.Priority).Distinct().Count().ShouldBe(All.Count);
        All.ShouldAllBe(c => c.Width > 0);
    }

    [Fact]
    public void A_column_can_be_found_by_the_key_that_was_remembered()
    {
        RegisterColumns.ByKey("memo").ShouldBe(RegisterColumns.Memo);
        RegisterColumns.ByKey("nonsense").ShouldBeNull();
    }
}
