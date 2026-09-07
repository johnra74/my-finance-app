# Tasks: Budgets

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — Storing a figure

- [X] **T001** Set a budget for a category and month, from any day of that month, stored
  positive, replacing rather than accumulating; clear it; refuse a category that is gone
  - Implements: `src/MyFinance.Data/Services/BudgetService.cs`,
    `src/MyFinance.Core/Entities/BudgetLine.cs`
  - Proven by: `ReportServiceTests.Any_day_of_the_month_sets_the_budget_for_that_month`,
    `A_budget_is_stored_positive_however_it_was_supplied`,
    `Setting_a_budget_twice_replaces_it_rather_than_adding_a_second`,
    `A_budget_can_be_cleared`, `Budgeting_a_category_that_is_gone_is_refused`,
    `Lines_come_back_in_category_order`
  - Requirements: FR-001 – FR-006, FR-014
- [X] **T002** Copy a month across the year, leaving existing figures alone
  - Proven by: `A_month_can_be_copied_across_a_year`,
    `Copying_leaves_an_existing_figure_alone_unless_told_otherwise`
  - Requirement: FR-012

## Phase 2 — Measuring against reality

- [X] **T003** Meters from budget and actual spending, with an honest negative remainder
  - Implements: `src/MyFinance.Core/Budgeting/BudgetCalculator.cs`
  - Proven by: `BudgetCalculatorTests.A_budget_is_measured_against_what_was_actually_spent`,
    `A_budget_reports_what_is_left`, `Going_over_is_reported_as_a_negative_remainder`,
    `A_zero_budget_that_has_been_spent_against_reads_as_fully_used`,
    `A_category_with_nothing_spent_is_untouched`, `A_month_with_nothing_in_it_is_still_a_month`
  - Requirements: FR-007, FR-008
- [X] **T004** One source of spending — the budget and the spending report agree
  - Proven by: `ReportServiceTests.A_budget_and_a_spending_report_agree_about_the_same_month`
  - Requirement: NFR-002
- [X] **T005** The over-budget count covers spending categories only; the average left over
  - Proven by: `The_over_budget_count_only_counts_spending_categories`,
    `The_average_says_what_is_typically_left_over`
  - Requirements: FR-015, FR-017

## Phase 3 — Rollover

- [X] **T006** Surplus carried forward per category; overspend never carried; a complete chain
  regardless of where the window starts
  - Implements: `BudgetCalculator`
  - Proven by: `An_unspent_remainder_carries_forward_when_asked`,
    `An_overspend_does_not_become_a_debt_against_the_next_month`,
    `Without_rollover_each_period_starts_afresh`,
    `The_rollover_chain_is_complete_even_when_the_window_starts_late`
  - Requirements: FR-009, FR-010, FR-011
- [X] **T007** Rollover verified against the real migrated book
  - Proven by: `ReportServiceTests.Rollover_carries_through_the_real_book`
  - Requirement: FR-011

## Phase 4 — Views

- [X] **T008** The year view: twelve months as one line per category
  - Implements: `src/MyFinance.App/ViewModels/Pages/BudgetPageViewModel.cs`, `BudgetCalculator`
  - Proven by: `A_year_rolls_twelve_months_into_one_line_per_category`
  - Requirement: FR-013
- [X] **T009** Watched categories on the dashboard, with or without a budget; watching twice
  unwatches
  - Implements: `BudgetService`, `src/MyFinance.App/ViewModels/Pages/HomePageViewModel.cs`
  - Proven by: `ReportServiceTests.A_watched_category_reports_how_it_is_tracking`,
    `A_watched_category_with_no_budget_still_reports_what_was_spent`,
    `Watching_a_category_twice_stops_watching_it`
  - Requirement: FR-016
- [X] **T010** An empty budget produces an empty result
  - Proven by: `A_book_with_no_budget_produces_nothing_rather_than_throwing`
  - Requirement: FR-001

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 – FR-006 | T001, T010 | ✅ |
| FR-007, FR-008 | T003 | ✅ |
| FR-009 – FR-011 | T006, T007 | ✅ |
| FR-012 | T002 | ✅ |
| FR-013 | T008 | ✅ |
| FR-014 | T001 | ✅ |
| FR-015, FR-017 | T005 | ✅ |
| FR-016 | T009 | ✅ |
| NFR-001 | By construction: `BudgetCalculator` is in Core | ✅ |
| NFR-002 | T004 | ✅ |
