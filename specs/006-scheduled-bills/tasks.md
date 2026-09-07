# Tasks: Scheduled bills and the cash-flow forecast

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — Recurrence arithmetic

- [X] **T001** Every frequency steps by its own period, with an interval multiplier
  - Implements: `src/MyFinance.Core/Scheduling/{RecurrenceRule,RecurrenceCalculator}.cs`
  - Proven by: `RecurrenceCalculatorTests.Each_frequency_steps_by_its_own_period`,
    `An_interval_multiplies_the_period`, `A_one_off_happens_exactly_once`
  - Requirement: FR-001
- [X] **T002** Occurrences computed from the anchor: short months clamp and recover, leap days
  fall back, clock changes are irrelevant
  - Proven by: `The_thirty_first_is_clamped_in_a_short_month`,
    `A_bill_due_on_the_thirty_first_returns_to_the_thirty_first_after_a_short_month`,
    `A_yearly_bill_on_the_leap_day_falls_back_to_the_twenty_eighth`,
    `February_gets_its_extra_day_in_a_leap_year`, `A_clock_change_cannot_move_a_due_date`
  - Requirements: FR-002, FR-003, FR-007, NFR-002
- [X] **T003** Weekend shifts that never become the anchor
  - Proven by: `A_weekend_date_moves_according_to_the_policy`,
    `A_shift_never_becomes_the_anchor_for_the_next_occurrence`,
    `A_shifted_series_still_returns_every_occurrence_in_a_window`
  - Requirement: FR-004
- [X] **T004** End conditions: none, by date, by count — keeping a last occurrence a shift
  pushes past the end
  - Proven by: `A_series_with_no_end_keeps_going`, `An_end_date_stops_the_series`,
    `A_count_stops_the_series`, `A_finished_series_has_no_next_date`,
    `The_last_occurrence_is_kept_even_when_a_shift_pushes_it_past_the_end`
  - Requirement: FR-005
- [X] **T005** Twice-a-month: two alternating days, later-day start, equal days degrading
  - Proven by: `The_two_days_alternate_through_the_months`,
    `Starting_on_the_later_day_continues_from_the_earlier_day_next_month`,
    `The_second_day_defaults_to_a_fortnight_later`,
    `Two_identical_days_degrade_to_monthly_rather_than_repeating_a_date`
  - Requirement: FR-006
- [X] **T006** Windows and counting: inclusive ends, nothing before the start, next due date
  - Proven by: `Counting_a_window_includes_both_ends`, `A_window_before_the_series_starts_is_empty`,
    `Nothing_before_the_window_is_projected_into_it`,
    `The_next_due_date_is_the_first_on_or_after_today`, `Moving_past_the_end_stops_at_the_end`,
    `Nine_occurrences_fall_past_due_over_four_months`
  - Requirements: FR-002, FR-010
- [X] **T007** [P] Describing a recurrence the way the reference application words it
  - Implements: `src/MyFinance.Core/Scheduling/RecurrenceDescriber.cs`
  - Proven by: `Frequencies_read_the_way_the_reference_books_word_them`,
    `An_interval_is_spelled_out`, `The_end_condition_is_spelled_out`
  - Requirement: FR-008

## Phase 2 — The forecast

- [X] **T008** Day-by-day projection from the window's opening balance, netting income and
  outgoings, finding the low point
  - Implements: `src/MyFinance.Core/Scheduling/CashFlowForecaster.cs`
  - Proven by: `CashFlowForecasterTests.The_forecast_projects_scheduled_bills_forward`,
    `The_opening_balance_is_taken_at_the_start_of_the_window`,
    `A_monthly_bill_walks_the_balance_down`, `Income_and_outgoings_net_off_by_day`,
    `The_forecast_finds_the_point_where_the_account_would_go_short`,
    `The_lowest_point_is_found_even_when_the_close_looks_healthy`,
    `A_projection_that_never_dips_reports_no_trouble`,
    `An_empty_schedule_still_draws_the_opening_balance`
  - Requirements: FR-023, FR-024, FR-025
- [X] **T009** Already-entered occurrences excluded; ended series stop contributing; all
  accounts can be projected together; estimates carried through
  - Proven by: `A_bill_already_entered_is_not_projected_again`,
    `A_series_that_ends_stops_contributing`, `A_forecast_across_every_account_adds_them_together`,
    `A_twice_monthly_deposit_is_projected_twice_a_month`,
    `Estimates_are_carried_through_so_the_projection_can_say_so`
  - Requirements: FR-011, FR-026, FR-027

## Phase 3 — The schedule itself

- [X] **T010** Create and read back a series; deactivate without losing it; refuse an amountless
  non-estimate; refuse an unmodelled account; refuse an allocation that does not add up
  - Implements: `src/MyFinance.Data/Services/ScheduleService.cs`
  - Proven by: `ScheduleServiceTests.A_bill_can_be_scheduled_and_read_back`,
    `An_inactive_bill_drops_off_the_list_without_being_lost`,
    `A_bill_with_no_amount_is_refused_unless_it_is_an_estimate`,
    `Bills_cannot_be_scheduled_against_an_imported_balance_only_account`,
    `Categories_that_do_not_add_up_to_the_bill_are_refused`
  - Requirements: FR-012, FR-016, FR-022
- [X] **T011** The bills summary: overdue first, then by due date, saying how far behind and
  how many occurrences
  - Proven by: `Overdue_bills_come_first_then_the_rest_by_due_date`,
    `An_overdue_bill_reports_how_far_behind_it_is`,
    `A_twice_monthly_deposit_counts_its_own_backlog`,
    `Estimated_amounts_are_flagged_through_to_the_projection`
  - Requirements: FR-009, FR-010, FR-011

## Phase 4 — Entering

- [X] **T012** Enter on the day it was due; enter a backlog each on its own day; copy the
  category template; allow a different actual amount
  - Proven by: `Entering_a_bill_writes_it_into_the_register_on_the_day_it_was_due`,
    `A_backlog_can_be_entered_in_one_action_with_each_on_its_own_day`,
    `The_category_template_is_copied_onto_the_generated_transaction`,
    `A_different_actual_amount_is_taken_by_a_single_category`,
    `A_different_actual_amount_on_a_split_bill_goes_to_the_first_line`,
    `An_entered_bill_stops_being_overdue_and_moves_to_the_next_date`
  - Requirements: FR-013, FR-014, FR-016, FR-017
- [X] **T013** Skip an occurrence without writing anything
  - Proven by: `Skipping_clears_an_occurrence_without_writing_anything`
  - Requirement: FR-015
- [X] **T014** Occurrence history records decisions, not dates; no double entry; the pattern
  can move without disturbing what was paid
  - Proven by: `The_history_records_what_happened_to_each_occurrence`,
    `Entering_the_same_bill_twice_does_not_record_it_twice`,
    `Moving_the_pattern_does_not_disturb_what_has_already_been_paid`
  - Requirements: FR-018, FR-019
- [X] **T015** Auto-entry: only where asked for, reaching forward by that series' setting,
  idempotent
  - Proven by: `Auto_entry_only_touches_series_that_asked_for_it`,
    `Auto_entry_reaches_forward_by_the_days_ahead_setting`,
    `Running_auto_entry_twice_does_not_double_up`
  - Requirement: FR-020
- [X] **T016** Deleting a series leaves its transactions
  - Proven by: `Deleting_a_schedule_leaves_the_transactions_it_produced`
  - Requirement: FR-021

## Phase 5 — The screens

- [X] **T017** Bills summary, calendar strip of five months, cash-flow page
  - Implements: `src/MyFinance.App/ViewModels/Pages/{Bills,Forecast}PageViewModel.cs` and
    their views
  - Proven by: the arithmetic behind them is covered by T008–T011.
    **Needs Windows:** the calendar strip's layout and the balance footer.
  - Requirement: FR-028

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T001 | ✅ |
| FR-002, FR-003, FR-007 | T002, T006 | ✅ |
| FR-004 | T003 | ✅ |
| FR-005 | T004 | ✅ |
| FR-006 | T005 | ✅ |
| FR-008 | T007 | ✅ |
| FR-009 – FR-011 | T011, T009 | ✅ |
| FR-012, FR-016, FR-022 | T010 | ✅ |
| FR-013, FR-014, FR-017 | T012 | ✅ |
| FR-015 | T013 | ✅ |
| FR-018, FR-019 | T014 | ✅ |
| FR-020 | T015 | ✅ |
| FR-021 | T016 | ✅ |
| FR-023 – FR-025 | T008 | ✅ |
| FR-026, FR-027 | T009 | ✅ |
| FR-028 | T017 | ⚠️ layout needs Windows |
| NFR-001 | By construction: Core has no database reference | ✅ |
| NFR-002 | T002 | ✅ |
