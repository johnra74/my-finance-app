# Tasks: Printing

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** **complete**

New coverage: `tests/MyFinance.Core.Tests/Printing/` — **28 tests** across `PageLayoutTests`
and `ColumnFitterTests`. The suite went from 922 to **950**.

Five things differed from the plan and are recorded at the end of `plan.md`; the first is that
a `FixedDocument` — not the `FlowDocument` the plan named — is what the plan's own central
decision actually implies.

This feature persists nothing but a remembered column selection, so there is no
`data-model.md`.

`[ ]` outstanding · `[P]` may run in parallel with its neighbours.

**Read the plan's testability table before starting.** This is the only feature in the
repository whose output cannot be verified in CI, and the split between what is tested and
what needs a person is deliberate rather than incidental.

## Phase 1 — Everything decidable, in Core

- [X] **T001** Pagination: rows per page, where breaks fall, headings repeated, page numbering
  with a known total
  - Implements: `src/MyFinance.Core/Printing/PageLayout.cs`
  - Proven by: `PageLayoutTests.Rows_fill_a_page_and_the_remainder_starts_the_next`,
    `Pages_are_numbered_with_their_total`, `A_single_short_page_is_not_split`,
    `An_empty_result_produces_one_page_saying_so`,
    `An_exactly_full_page_does_not_produce_an_empty_one_after_it` (the off-by-one that shows
    up as a blank final sheet), `Rows_per_page_comes_from_the_space_left_after_the_heading_and_footer`,
    `A_page_too_small_for_even_one_row_still_takes_one`, `Landscape_holds_fewer_rows_than_portrait`,
    `A_nonsensical_row_height_is_refused_rather_than_producing_no_pages`
  - **`Column_headings_repeat_on_every_page` was not written**: the heading band is drawn above
    every page's rows by construction, so there is no "first page only" path for a test to
    catch. Asserting it would test the absence of code nobody wrote.
  - Requirement: FR-003
- [X] **T002** Column fitting: a default set that fits a given width, and the user's choice
  honoured
  - Implements: `src/MyFinance.Core/Printing/ColumnFitter.cs`
  - Proven by: `ColumnFitterTests.The_default_columns_fit_a_portrait_page`,
    `A_wider_page_admits_more_columns`,
    `A_column_the_user_chose_is_never_dropped_to_make_room` — the printout may be cramped, but
    it may not silently disagree with what was asked for —
    `The_columns_a_register_is_printed_for_always_survive`, `The_memo_is_the_first_thing_to_go`,
    `Columns_print_in_the_registers_own_order_however_they_were_chosen`,
    `What_was_left_out_is_always_reported`, `At_least_one_column_survives_the_narrowest_page`,
    `Every_register_column_has_a_distinct_key_and_priority`
  - Requirement: FR-006
- [X] **T003** The printed row set equals the filtered row set
  - Implements: `src/MyFinance.Core/Printing/PageLayout.cs`
  - Proven by: `PageLayoutTests.Every_row_given_appears_on_exactly_one_page` and
    `No_row_is_lost_or_duplicated_at_any_page_size` (five row-count/page-size combinations) —
    the property that catches a pagination bug dropping or duplicating a row, which on paper
    nobody notices
  - Requirements: FR-001, NFR-002

## Phase 2 — The documents

- [X] **T004** A register document: heading naming the account and period, the chosen columns,
  paginated by T001
  - Implements: `src/MyFinance.App/Printing/RegisterDocument.cs`,
    `src/MyFinance.App/Printing/PrintedTable.cs`
  - Consumes the **already-formatted** rows from
    `src/MyFinance.App/ViewModels/Pages/RegisterPageViewModel.cs`. NFR-002 forced one change
    there: the grid was formatting the date with `StringFormat=d` while money already used
    shared `…Text` properties, so `DateText` was added and the grid now binds to it. One
    formatting path, screen and paper.
  - The filter in force is named on the printout — a printed register quietly showing a subset
    would mislead anybody checking it against a statement.
  - Proven by: pagination and columns by T001–T003. **Needs Windows and a person** for
    legibility and alignment.
  - Requirements: FR-001, NFR-002
- [X] **T005** A report document: the table, plus the chart **redrawn at print resolution**
  from `src/MyFinance.Core/Reporting/ChartGeometry.cs`
  - Implements: `src/MyFinance.App/Printing/ReportDocument.cs`
  - The chart prints on its own sheet rather than above the table, so every page has the same
    row capacity. Bar lengths come from `BarSlice.Fraction` — the same figures the screen uses,
    already covered by `tests/MyFinance.Core.Tests/Reporting/ChartGeometryTests.cs`.
  - Proven by: the geometry as above. **Needs Windows and a person** for the rendering.
  - Requirement: FR-002
- [X] **T006** Greyscale legibility: direct labels rather than hue alone
  - Implements: `ReportDocument` — every bar carries its label and its figure beside it, and
    bars print in **one ink** rather than the screen's categorical palette. On paper the series
    are told apart by the text next to them; a printed rainbow would only invite the reader to
    match slices against a legend greyscale has already flattened.
  - Proven by: **needs a person and a printer.** No automated test can assert this, and the
    coverage table says so rather than implying otherwise.
  - Requirement: FR-005

## Phase 3 — Running it

- [X] **T007** Preview with page setup, **stating the page count before anything spools**
  - Implements: `src/MyFinance.App/Printing/PrintCommand.cs`,
    `src/MyFinance.App/Views/Dialogs/PrintPreviewWindow.xaml(.cs)`, and the **Print…** buttons
    on `Views/Pages/RegisterPage.xaml` and `Views/Pages/ReportsPage.xaml`
  - The page count is on screen from the moment the preview opens, and above 50 pages it says
    so in as many words. A twenty-five-year register is thousands of pages and somebody will
    ask for one by accident; the moment to find out is before the paper moves.
  - The printer's own printable area rebuilds the document rather than scaling it, so a row is
    never half-clipped.
  - Proven by: the count by T001. **Needs Windows** for the preview itself.
  - Requirement: FR-004
- [X] **T008** Off the interface thread, and cancellable
  - Implements: `PrintPreviewWindow` — spooling through
    `XpsDocumentWriter.WriteAsync`, cancelled if the window closes mid-job.
  - ⚠️ **Partially met, and worth being precise about.** Composing the pages still happens on
    the UI thread: WPF elements can only be built where they live, so `RunBusyAsync` cannot
    help here. What the async writer avoids is the window freezing while a long job is queued.
    NFR-001 holds for spooling, not for composition.
  - Proven by: **needs Windows.**
  - Requirement: NFR-001
- [X] **T009** [P] Remember the column selection and page setup
  - Implements: `src/MyFinance.Data/Services/SettingsService.cs`
    (`PrintColumnsKey`, `PrintPageSetupKey`), read at print time by
    `RegisterPageViewModel.PrintColumnsAsync`
  - Read at print time rather than cached, so a change applies to the next register without
    the page having to be revisited. Absent means "decide for me".
  - ⚠️ **No editor for it yet.** The setting is honoured wherever it is set, but nothing in the
    interface writes it — so today the fitter always decides. Noted below rather than left to
    be discovered.
  - Proven by: `ColumnFitterTests.A_column_the_user_chose_is_never_dropped_to_make_room` covers
    the behaviour; `SettingsService`'s existing round-trip coverage covers the storage.
  - Requirement: FR-006

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T003, T004 | ✅ arithmetic tested; ⚠️ **appearance needs a person** |
| FR-002 | T005 | ✅ geometry covered; ⚠️ **rendering needs a person** |
| FR-003 | T001 | ✅ fully tested |
| FR-004 | T007 | ✅ count tested; ⚠️ **preview needs Windows** |
| FR-005 | T006 | ⚠️ **no automated test is possible** |
| FR-006 | T002, T009 | ✅ fitting tested; ⚠️ **no editor writes the setting yet** |
| NFR-001 | T008 | ⚠️ **spooling only — composition stays on the UI thread** |
| NFR-002 | T003, T004 | ✅ one formatting path, enforced by `DateText` |
| SC-001 | T003 | ✅ row set tested; ⚠️ **the printed page needs a person** |
| SC-002 | T001 | ✅ fully tested |
| SC-003 | T006 | ⚠️ **needs a person and a printer** |

**Six of eleven rows carry a ⚠️, and three of them cannot be closed by any test this
repository can run.** That was true when this was planned and it is still true; it is a
property of the feature rather than an oversight.

## What is not covered

- **Everything that has to be looked at**: legibility, alignment, whether the chart reads in
  greyscale, whether the preview window lays out. A person with a printer closes these, nobody
  else.
- **`NFR-001` for composition.** Only spooling is off the UI thread. Building thousands of
  pages of `TextBlock`s will block, and no amount of `RunBusyAsync` changes that — a genuinely
  huge register would need a virtualised paginator, which is a different piece of work.
- **The column-selection editor.** `FR-006` is honoured by the fitter and by the stored
  setting, but nothing yet lets the user write it. Until something does, the default is always
  what prints.
