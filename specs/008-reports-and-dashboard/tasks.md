# Tasks: Reports, charts and the dashboard

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — The engine

- [X] **T001** Grouping by category and by payee, largest first, with shares
  - Implements: `src/MyFinance.Core/Reporting/{ReportEngine,ReportModels,ReportEntry}.cs`
  - Proven by: `ReportEngineTests.Outgoings_group_by_category_largest_first`,
    `Outgoings_group_by_who_was_paid`, `Shares_are_positive_and_add_up_to_one`,
    `Transactions_with_no_payee_are_grouped_under_a_label_of_their_own`,
    `Income_is_left_out_of_a_spending_report`, `Subcategories_can_be_rolled_up_into_their_heading`
  - Requirements: FR-001, FR-003, FR-008, FR-009, FR-010
- [X] **T002** Exclusions: transfers, voids, and transfers on request
  - Proven by: `A_transfer_between_your_own_accounts_never_appears_as_spending`,
    `Transfers_can_be_asked_for_explicitly`, `Void_rows_contribute_nothing`,
    `ReportServiceTests.A_credit_card_payment_is_a_transfer_and_never_appears`,
    `Spending_on_a_credit_card_counts_the_same_as_spending_from_a_current_account`
  - Requirements: FR-005, FR-006
- [X] **T003** Splits counted once per category and once overall
  - Proven by: `A_split_transaction_reaches_both_of_its_categories`,
    `A_split_transaction_is_counted_once_per_category_but_once_overall`
  - Requirement: FR-007
- [X] **T004** Filters: date range at both ends, account filter, empty filter meaning all
  - Proven by: `A_date_range_bounds_the_report_at_both_ends`,
    `An_account_filter_restricts_to_those_accounts`, `An_empty_account_filter_means_all_of_them`,
    `A_category_filter_excludes_uncategorized_rows_by_construction`,
    `Uncategorized_rows_can_be_excluded_altogether`
  - Requirements: FR-002, FR-016
- [X] **T005** Folding a long tail rather than truncating it
  - Proven by: `A_long_tail_is_folded_rather_than_truncated`, `A_short_report_is_not_folded`
  - Requirement: FR-011
- [X] **T006** Time series by month, quarter and year; income and spending separately
  - Proven by: `Income_and_spending_are_reported_month_by_month`,
    `Income_and_spending_are_reported_separately_per_month`,
    `Quarters_and_years_group_as_they_should`, `A_period_totals_its_spending_and_its_income_separately`
  - Requirements: FR-001, FR-004
- [X] **T007** Period comparison, with no percentage where there is no denominator
  - Proven by: `A_comparison_measures_the_window_against_the_one_before_it`,
    `Two_windows_are_compared_category_by_category`,
    `A_category_that_appeared_from_nothing_has_no_percentage`
  - Requirement: FR-012
- [X] **T008** Account balances and net worth over time
  - Proven by: `Account_balances_are_grouped_and_signed_as_the_account_list_shows_them`,
    `Net_worth_over_time_nets_debt_against_what_is_held`, `Debt_reduces_net_worth`,
    `Every_movement_up_to_a_date_counts_not_only_those_in_the_window`
  - Requirements: FR-013, FR-014
- [X] **T009** Nothing throws on nothing
  - Proven by: `An_empty_book_reports_nothing_rather_than_throwing`,
    `An_empty_result_is_reported_as_empty_rather_than_throwing`,
    `A_report_of_zeroes_does_not_divide_by_zero`
  - Requirement: NFR-001

## Phase 2 — Honesty about what is unassigned

- [X] **T010** Uncategorized spending reported above the chart, and drillable
  - Implements: `ReportEngine`, `src/MyFinance.Data/Services/ReportService.cs`
  - Proven by: `Uncategorized_spending_is_surfaced_rather_than_hidden_in_the_percentages`,
    `ReportServiceTests.Uncategorized_spending_is_reported_above_the_chart`,
    `Drilling_into_the_uncategorized_row_finds_what_needs_filing`
  - Requirements: FR-015, FR-017

## Phase 3 — Chart geometry

- [X] **T011** Bars, scales, ticks and points, computed where they can be tested
  - Implements: `src/MyFinance.Core/Reporting/ChartGeometry.cs`
  - Proven by: `ChartGeometryTests.Bars_are_scaled_against_the_longest_not_the_total`,
    `The_axis_tops_out_at_a_round_number`, `A_scale_top_rounds_to_a_figure_a_person_would_choose`,
    `The_scale_always_includes_zero`, `A_series_that_goes_negative_gets_a_scale_below_zero`,
    `Ticks_are_evenly_spaced_from_the_baseline`, `Both_series_share_one_scale`,
    `Higher_values_sit_higher_on_the_screen`, `Points_run_left_to_right_across_the_viewport`,
    `A_single_point_sits_in_the_middle`, `A_flat_series_does_not_divide_by_zero`,
    `An_empty_report_produces_an_empty_chart_rather_than_throwing`,
    `An_empty_series_produces_an_empty_chart`, `The_polyline_string_is_culture_independent`,
    `Each_bar_keeps_its_key_so_a_click_can_drill_into_it`
  - Requirements: FR-019 – FR-025
- [X] **T012** Chart and table always both, and a validated palette
  - Implements: the report views in `src/MyFinance.App/Views/Pages`
  - Proven by: geometry by T011; the palette by measurement (ΔE 24.7 protanopia / 33.6 normal,
    ≥3:1 on white). **Needs Windows:** that both are laid out together.
  - Requirements: FR-018, FR-026, NFR-003

## Phase 4 — Getting behind a figure, and out of the application

- [X] **T013** Drill-down from a report row to its transactions, and on into the register
  - Implements: `ReportService`, `src/MyFinance.App/ViewModels/Pages/ReportsPageViewModel.cs`
  - Proven by: `ReportServiceTests.Drilling_into_a_category_lists_the_transactions_behind_it`,
    `Spending_groups_by_category_over_a_real_book`, `Spending_groups_by_payee`
  - Requirement: FR-027
- [X] **T014** CSV export of any report
  - Implements: `ReportsPageViewModel`
  - Proven by: **not covered by an automated test** — the export path runs in the view model.
    The figures it writes are covered by T001–T008.
  - Requirement: FR-028

## Phase 5 — The dashboard

- [X] **T015** Home page: net worth, favourite accounts, overdue and upcoming, last thirty
  days, watched categories
  - Implements: `src/MyFinance.App/ViewModels/Pages/HomePageViewModel.cs`
  - Proven by: the underlying figures by T008 and by `ReportServiceTests.A_watched_category_reports_how_it_is_tracking`,
    `A_watched_category_with_no_budget_still_reports_what_was_spent`.
    **Needs Windows:** the tile layout.
  - Requirement: FR-029

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T001, T006, T008 | ✅ |
| FR-002, FR-016 | T004 | ✅ |
| FR-003, FR-008 – FR-010 | T001 | ✅ |
| FR-004 | T006 | ✅ |
| FR-005, FR-006 | T002 | ✅ |
| FR-007 | T003 | ✅ |
| FR-011 | T005 | ✅ |
| FR-012 | T007 | ✅ |
| FR-013, FR-014 | T008 | ✅ |
| FR-015, FR-017 | T010 | ✅ |
| FR-018, FR-026 | T012 | ⚠️ layout needs Windows; palette measured |
| FR-019 – FR-025 | T011 | ✅ |
| FR-027 | T013 | ✅ |
| FR-028 | T014 | ⚠️ **no automated test over the CSV writer itself** |
| FR-029 | T015 | ⚠️ layout needs Windows |
| NFR-001 | By construction: engine and geometry are in Core | ✅ |
| NFR-002 | `ReportServiceTests` run against the real ~20k-row book | ✅ |
| NFR-003 | T012 | ✅ |
