# Feature Specification: Reports, charts and the dashboard

**Folder:** `008-reports-and-dashboard`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "Where did the money go? Show me by category, by payee, over time, and let me get
behind any figure to the transactions that made it."

## User Scenarios & Testing

### Primary user story

The point of keeping books is being able to ask questions of them. The user wants spending
grouped the obvious ways, over a window they choose, as a chart *and* a table — and, crucially,
to be able to double-click a figure and see the transactions behind it, because the point of a
report is usually the question it raises. When a large share of the window is uncategorized,
they need to be told, because percentages computed over a pile of unassigned spending are
quietly wrong.

### Acceptance scenarios

1. **Given** a date range, **When** a spending-by-category report is run, **Then** outgoings
   are grouped by category, largest first, with each group's share of the total.
2. **Given** a report, **When** it is displayed, **Then** both a chart and a table are shown.
   Neither on its own is the report.
3. **Given** a report row, **When** it is double-clicked, **Then** the transactions behind it
   are listed; double-clicking one of those lands on it in the register.
4. **Given** a window with $2,000 of uncategorized spending, **When** the report is shown,
   **Then** that figure is stated above the chart, with an offer to list it — not folded
   silently into the percentages.
5. **Given** a transfer between the user's own accounts, **When** any spending report is run,
   **Then** it never appears. Moving your own money is not spending.
6. **Given** a split transaction, **When** a category report is run, **Then** it reaches both
   of its categories, and is counted once overall.
7. **Given** any report, **When** the user exports, **Then** a CSV is produced.
8. **Given** the home page, **When** it is opened, **Then** it shows net worth, favourite
   accounts, what is overdue and what falls due in the next month, where the money went over
   the last thirty days, and any watched category.

### Edge cases

- An empty book, an empty result, a report of zeroes, a flat series, a single point → all
  produce an empty or degenerate chart rather than throwing or dividing by zero.
- A long tail of small categories → **folded**, not truncated; a short report is not folded.
- A category that appeared from nothing in a comparison → no percentage change, because there
  is no denominator.
- A series that goes negative → the scale extends below zero, and the scale always includes
  zero.
- Transactions with no payee → grouped under a label of their own rather than dropped.
- Voided rows → contribute nothing.
- Credit-card spending → counted the same as spending from a current account; a credit-card
  *payment* is a transfer and never appears.

## Requirements

### Functional requirements

**The reports**

- **FR-001**: The system MUST provide eight reports: spending by category, spending by payee,
  income by category, income and spending over time, this period against the last, account
  balances, net worth over time, and every transaction.
- **FR-002**: Each report MUST take a date range bounding it at both ends, and MUST optionally
  restrict to one or more accounts — an empty account filter meaning all of them.
- **FR-003**: The system MUST offer rolling subcategories up under their heading.
- **FR-004**: The system MUST group time series by month, quarter and year.
- **FR-005**: The system MUST exclude transfers from spending and income by default, and MUST
  allow them to be asked for explicitly.
- **FR-006**: The system MUST exclude voided rows from every report.
- **FR-007**: The system MUST count a split transaction once per category, and once overall.
- **FR-008**: The system MUST keep income out of a spending report, and vice versa.
- **FR-009**: The system MUST report shares that are positive and sum to one.
- **FR-010**: The system MUST group transactions with no payee under a label of their own.
- **FR-011**: The system MUST fold a long tail rather than truncating it, so the total still
  adds up.
- **FR-012**: The system MUST compute a comparison window against the one immediately before
  it, category by category, and MUST NOT report a percentage change for a category that
  appeared from nothing.
- **FR-013**: The system MUST compute net worth by netting debt against what is held, counting
  **every** movement up to a date rather than only those inside the window.
- **FR-014**: The system MUST group and sign account balances the way the account list shows
  them.

**Honesty about uncategorized spending**

- **FR-015**: The system MUST report uncategorized spending above the chart, rather than
  hiding it in the percentages.
- **FR-016**: The system MUST allow uncategorized rows to be excluded altogether, and a
  category filter MUST exclude them by construction.
- **FR-017**: The system MUST let the user drill into the uncategorized row to find what needs
  filing.

**Charts**

- **FR-018**: The system MUST show a chart and a table for every report. Having both is also
  what makes a report readable without relying on colour.
- **FR-019**: Chart geometry — bar lengths, scales, gridline positions, polyline points — MUST
  be computed where it can be tested, not in the layer that can only be checked by eye.
- **FR-020**: Bars MUST be scaled against the longest, not against the total.
- **FR-021**: An axis MUST top out at a round number a person would choose, and MUST always
  include zero.
- **FR-022**: Two series on one chart MUST share one scale. A second y-axis invents a
  relationship the figures do not have.
- **FR-023**: Higher values MUST sit higher on the screen, and points MUST run left to right
  across the viewport.
- **FR-024**: Every bar MUST keep its key, so a click can drill into it.
- **FR-025**: Point strings MUST be culture-independent.
- **FR-026**: The system MUST NOT use colour as the only carrier of meaning; a status colour
  MUST always be accompanied by the same fact written out.

**Getting behind a figure**

- **FR-027**: The system MUST list the transactions behind any report row, and MUST navigate
  from one of those to its position in the register.

**Export**

- **FR-028**: The system MUST export any report to CSV, at any time.

**The dashboard**

- **FR-029**: The home page MUST show net worth, favourite accounts, what is overdue and what
  falls due within the next month, where the money went over the last thirty days, and any
  watched category.

### Non-functional requirements

- **NFR-001**: The report engine and chart geometry MUST be pure functions in a
  platform-neutral project, tested without a window.
- **NFR-002**: Reports MUST run acceptably over a book of ~20,000 transactions, and MUST run
  off the interface thread.
- **NFR-003**: The chart palette MUST be verified for colour-vision deficiency and contrast by
  measurement, not by eye.

## Key Entities

- **Report group** — a key (category or payee id, or null for unassigned), a label, a signed
  total, a count, and a share.
- **Period total** — a period start, a label, income as positive and spending as negative,
  with the net derived.
- **Account balance row**, **net worth point**, **comparison row** — the shapes the other
  reports produce.

## Success Criteria

- **SC-001**: A transfer between the user's own accounts never appears as spending, and a
  credit-card payment never appears.
  *`ReportEngineTests.A_transfer_between_your_own_accounts_never_appears_as_spending`,
  `ReportServiceTests.A_credit_card_payment_is_a_transfer_and_never_appears`.*
- **SC-002**: A split transaction reaches both its categories and is counted once overall.
  *`A_split_transaction_reaches_both_of_its_categories`,
  `A_split_transaction_is_counted_once_per_category_but_once_overall`.*
- **SC-003**: Shares are positive and sum to one.
  *`ReportEngineTests.Shares_are_positive_and_add_up_to_one`.*
- **SC-004**: Uncategorized spending is surfaced rather than hidden in the percentages.
  *`Uncategorized_spending_is_surfaced_rather_than_hidden_in_the_percentages`,
  `Uncategorized_spending_is_reported_above_the_chart`.*
- **SC-005**: A long tail is folded, not truncated — the total still adds up.
  *`A_long_tail_is_folded_rather_than_truncated`, `A_short_report_is_not_folded`.*
- **SC-006**: Bars are scaled against the longest, the axis tops at a round number, the scale
  always includes zero, two series share one scale, and higher values sit higher.
  *`ChartGeometryTests.Bars_are_scaled_against_the_longest_not_the_total`,
  `The_axis_tops_out_at_a_round_number`, `A_scale_top_rounds_to_a_figure_a_person_would_choose`,
  `The_scale_always_includes_zero`, `Both_series_share_one_scale`,
  `Higher_values_sit_higher_on_the_screen`, `A_series_that_goes_negative_gets_a_scale_below_zero`.*
- **SC-007**: Every degenerate input produces an empty chart rather than an error.
  *`An_empty_report_produces_an_empty_chart_rather_than_throwing`,
  `An_empty_series_produces_an_empty_chart`, `A_report_of_zeroes_does_not_divide_by_zero`,
  `A_flat_series_does_not_divide_by_zero`, `A_single_point_sits_in_the_middle`,
  `An_empty_book_reports_nothing_rather_than_throwing`.*
- **SC-008**: Drilling in works both ways down.
  *`ReportServiceTests.Drilling_into_a_category_lists_the_transactions_behind_it`,
  `Drilling_into_the_uncategorized_row_finds_what_needs_filing`,
  `ChartGeometryTests.Each_bar_keeps_its_key_so_a_click_can_drill_into_it`.*
- **SC-009**: Net worth counts every movement up to a date, and debt reduces it.
  *`Every_movement_up_to_a_date_counts_not_only_those_in_the_window`, `Debt_reduces_net_worth`,
  `Net_worth_over_time_nets_debt_against_what_is_held`.*
- **SC-010**: The two-series palette measures at ΔE 24.7 worst adjacent separation under
  protanopia and 33.6 for normal vision, both above 3:1 contrast on white. *Measured with the
  palette validator, not judged by eye.*

## Assumptions

- Depends on constitution principles **4** (the engine and the geometry are pure and in
  `MyFinance.Core` — "a bar whose length disagrees with its label is a lie the eye cannot
  catch"), **1** (integer cents), **10** (any report exports), **9** (reports run off the
  interface thread).
- Every transaction has at least one split (`002-accounts-and-register`), so reports aggregate
  over one uniform table with no special cases.
- Charts are drawn by hand in WPF rather than by a charting library, so the geometry is ours
  to test.

## Out of Scope

- A pie chart for spending by category. Deliberately: these category names are long
  (`Bills : Water and Sewer`) and a pie pushes them into a legend the reader has to match
  back to slices by colour. Bars keep the name against its length.
- A dual-axis chart. Ever. See FR-022.
- User-defined custom reports.
- Printing a report — see `016-printing`. CSV plus a spreadsheet is the answer today.
- Tax reporting beyond the tax-related flag. See the gap table in `specs/README.md`.
