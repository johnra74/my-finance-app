# Tasks: Accounts and the register

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — The money type everything else rests on

- [X] **T001** `Money` over integer minor units: arithmetic, comparison, formatting, parsing,
  allocation without losing a cent
  - Implements: `src/MyFinance.Core/Primitives/Money.cs`
  - Proven by: `tests/MyFinance.Core.Tests/Primitives/MoneyTests.cs` — including
    `FromDecimal_rounds_half_away_from_zero_not_to_even`,
    `Repeated_addition_stays_exact_where_double_would_drift`,
    `Allocate_into_equal_parts_loses_no_cents`, `Overflow_is_detected_rather_than_wrapping`
  - Requirement: constitution principle 1

## Phase 2 — Balance arithmetic, off the UI thread and out of WPF

- [X] **T002** Running balance as a prefix sum over a totally-ordered register
  - Implements: `src/MyFinance.Core/Registers/BalanceCalculator.cs`
  - Proven by: `BalanceCalculatorTests.Running_balance_is_the_prefix_sum_of_amounts`,
    `Transactions_sharing_a_date_are_ordered_by_sequence_then_id`,
    `Register_order_is_stable_regardless_of_input_order`
  - Requirements: FR-011, FR-012, FR-013
- [X] **T003** Current, cleared and reconciled balances as three subsets; voids in none
  - Implements: `BalanceCalculator`
  - Proven by: `Current_cleared_and_reconciled_balances_each_count_a_different_subset`,
    `Balances_exclude_voided_transactions_at_every_cleared_level`,
    `Voided_transactions_stay_visible_but_do_not_move_the_balance`
  - Requirements: FR-004, FR-017
- [X] **T004** Balance as of a date, inclusive of that day
  - Implements: `BalanceCalculator`
  - Proven by: `Balance_as_of_a_date_ignores_later_transactions`,
    `Balance_as_of_includes_transactions_dated_that_exact_day`
  - Requirement: FR-011
- [X] **T005** Payment and deposit columns split on the sign
  - Implements: `BalanceCalculator`
  - Proven by: `Payment_and_deposit_columns_split_on_the_sign`
  - Requirement: FR-014
- [X] **T006** A whole month of running balances, on mixed amounts, without drift
  - Proven by: `Running_balances_survive_a_whole_month_of_mixed_amounts`
  - Requirement: SC-003

## Phase 3 — The account list

- [X] **T007** Grouping, subtotals, grand total, ordering, closed accounts last
  - Implements: `src/MyFinance.Core/Accounts/AccountListBuilder.cs`
  - Proven by: `AccountListBuilderTests` — `Groups_appear_in_the_order_the_banking_screen_shows_them`,
    `Subtotals_add_up_within_a_group_and_the_total_across_them`,
    `Accounts_sort_by_their_manual_order_then_by_name`,
    `Closed_accounts_sink_to_the_bottom_of_their_group`,
    `Empty_groups_are_left_out_rather_than_shown_with_nothing_under_them`
  - Requirements: FR-002, FR-003, FR-006
- [X] **T008** Bank balance separate from current balance; opening balance counted
  - Proven by: `AccountServiceTests.The_account_list_separates_the_bank_balance_from_the_current_balance`,
    `The_account_list_counts_the_opening_balance`, `AccountListBuilderTests.The_cleared_total_is_tracked_separately_from_the_current_total`
  - Requirements: FR-004, FR-005
- [X] **T009** Uncategorized count per account and across the book
  - Proven by: `AccountServiceTests.The_account_list_reports_how_much_still_needs_a_category`,
    `AccountListBuilderTests.The_uncategorized_count_adds_up_across_every_account`
  - Requirement: FR-010
- [X] **T010** Unmodelled imported accounts get their own heading
  - Proven by: `AccountListBuilderTests.Imported_accounts_that_are_not_modelled_get_their_own_heading`
  - Requirement: FR-034

## Phase 4 — Account lifecycle

- [X] **T011** Create, edit, unique non-blank name, type frozen once used
  - Implements: `src/MyFinance.Data/Services/AccountService.cs`
  - Proven by: `AccountServiceTests.An_account_can_be_created_and_read_back`,
    `Two_accounts_cannot_share_a_name`, `A_blank_name_is_refused`,
    `Renaming_onto_another_accounts_name_is_refused`, `An_account_keeps_its_own_name_when_edited`,
    `The_type_is_frozen_once_transactions_exist`
  - Requirements: FR-001, FR-007
- [X] **T012** Close, reopen, reorder
  - Proven by: `AccountServiceTests.Closing_an_account_hides_it_without_losing_it`,
    `Accounts_can_be_reordered`
  - Requirements: FR-001, FR-006
- [X] **T013** Delete: take the register with it, remove the far leg of every transfer, report
  a missing account cleanly
  - Proven by: `AccountServiceTests.Deleting_an_account_takes_its_register_with_it`,
    `Deleting_an_account_removes_the_far_leg_of_its_transfers`,
    `Deleting_an_account_that_is_gone_is_reported_cleanly`
  - Requirements: FR-008, FR-009, NFR-002

## Phase 5 — Entering and editing

- [X] **T014** Validation: at least one split, splits summing to the total, no date before
  1900, all failures reported together
  - Implements: `src/MyFinance.Core/Validation/TransactionValidator.cs`
  - Proven by: `TransactionValidatorTests` — `A_transaction_with_no_splits_is_rejected`,
    `Splits_that_do_not_sum_to_the_total_are_rejected`,
    `A_date_before_1900_is_rejected_as_a_misread_import`,
    `All_failures_are_reported_together_not_just_the_first`
  - Requirements: FR-019, FR-020, FR-024, FR-025
- [X] **T015** Enter, edit and delete a transaction; splits replaced rather than accumulated
  - Implements: `src/MyFinance.Data/Services/RegisterService.cs`
  - Proven by: `RegisterServiceTests.A_transaction_can_be_entered_and_shows_in_the_register`,
    `Entering_a_transaction_always_leaves_at_least_one_split`,
    `Editing_a_transaction_replaces_its_splits_rather_than_accumulating_them`,
    `A_deleted_transaction_is_gone_along_with_its_splits`,
    `Deleting_a_transaction_that_is_gone_is_reported_cleanly`
  - Requirements: FR-019, FR-021, NFR-002
- [X] **T016** Splits across several categories, refused unless they add up
  - Proven by: `RegisterServiceTests.A_split_transaction_records_every_allocation`,
    `Splits_that_do_not_add_up_to_the_total_are_refused`
  - Requirement: FR-020
- [X] **T017** Payee reuse and payee memory; a split teaches no single category
  - Proven by: `RegisterServiceTests.Payees_are_reused_rather_than_duplicated`,
    `A_payee_remembers_the_category_and_amount_last_used`,
    `A_split_transaction_teaches_the_payee_no_single_category`,
    `A_transaction_with_no_payee_is_allowed`
  - Requirements: FR-022, FR-023
- [X] **T018** Back-dating, stable ordering within a date, clearing a row
  - Proven by: `RegisterServiceTests.A_back_dated_entry_lands_in_the_right_place_in_the_register`,
    `The_running_balance_is_stable_for_transactions_sharing_a_date`,
    `Clearing_a_transaction_moves_the_bank_balance_only`
  - Requirements: FR-013, FR-004
- [X] **T019** Amount as magnitude plus direction in the editor
  - Implements: `src/MyFinance.App/ViewModels/Dialogs/TransactionEditorViewModel.cs`
  - Proven by: **needs Windows** — the arithmetic is covered by T001/T014; what cannot be
    tested here is the control layout
  - Requirement: FR-018
- [X] **T020** Refuse writes to a balance-only imported account
  - Proven by: `RegisterServiceTests.An_imported_balance_only_account_cannot_be_written_to`
  - Requirement: FR-034

## Phase 6 — Transfers

- [X] **T021** Two legs written, kept in step, removed together, never self-targeting
  - Implements: `RegisterService`
  - Proven by: `RegisterServiceTests.A_transfer_writes_a_matching_row_in_the_other_account`,
    `Changing_a_transfer_amount_moves_both_legs`,
    `Retargeting_a_transfer_moves_the_far_leg_to_the_new_account`,
    `Turning_a_transfer_into_an_ordinary_payment_removes_the_far_leg`,
    `Deleting_one_leg_of_a_transfer_deletes_the_other`,
    `Deleting_both_legs_at_once_is_not_an_error`,
    `A_transfer_to_the_same_account_is_refused`, `Voiding_a_transfer_voids_both_legs`
  - Requirements: FR-026, FR-027, FR-028
- [X] **T022** Transfer symmetry enforced over the whole book
  - Implements: `TransactionValidator`
  - Proven by: `TransactionValidatorTests.Transfer_pairs_always_net_to_zero_across_the_books`,
    `Transfer_legs_that_do_not_cancel_out_are_rejected`,
    `Transfer_legs_on_different_dates_are_rejected`, `A_transfer_pointing_at_itself_is_rejected`
  - Requirement: FR-026
- [X] **T023** Transfers excluded from the uncategorized worklist
  - Proven by: `RegisterServiceTests.Transfers_are_not_listed_as_needing_a_category`
  - Requirement: FR-029

## Phase 7 — Filtering, searching, reconciling

- [X] **T024** Filters that never disturb the balance column
  - Implements: `RegisterService`, `src/MyFinance.App/ViewModels/Pages/RegisterPageViewModel.cs`
  - Proven by: `RegisterServiceTests.Filtering_the_register_keeps_the_balance_column_meaningful`,
    `The_register_can_be_searched_by_payee`,
    `Assigning_a_category_clears_it_from_the_worklist`
  - Requirements: FR-015, FR-016
- [X] **T025** Reconcile: open on everything unreconciled, tally the difference, refuse an
  unbalanced finish, lock what was ticked, resume where it left off, ignore voids
  - Implements: `src/MyFinance.Data/Services/ReconcileService.cs`
  - Proven by: `ReconcileServiceTests` — `A_session_opens_with_everything_not_yet_reconciled`,
    `The_tally_is_zero_when_the_ticked_items_match_the_statement`,
    `The_tally_reports_what_is_still_missing`, `An_unbalanced_reconciliation_is_refused`,
    `Finishing_locks_the_ticked_items_down`,
    `A_second_reconciliation_starts_where_the_first_finished`,
    `Void_items_never_appear_in_a_reconciliation`,
    `Reconciling_an_account_that_is_gone_is_reported_cleanly`
  - Requirements: FR-030, FR-031, FR-032, FR-033, NFR-002

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001, FR-007 | T011, T012 | ✅ |
| FR-002, FR-003, FR-006 | T007, T012 | ✅ |
| FR-004, FR-005 | T003, T008, T018 | ✅ |
| FR-008, FR-009 | T013 | ✅ |
| FR-010 | T009 | ✅ |
| FR-011 – FR-013 | T002, T004, T018 | ✅ |
| FR-014 | T005 | ✅ |
| FR-015, FR-016 | T024 | ✅ |
| FR-017 | T003 | ✅ |
| FR-018 | T019 | ⚠️ layout needs Windows |
| FR-019 – FR-021 | T014, T015, T016 | ✅ |
| FR-022, FR-023 | T017 | ✅ |
| FR-024, FR-025 | T014 | ✅ |
| FR-026 – FR-029 | T021, T022, T023 | ✅ |
| FR-030 – FR-033 | T025 | ✅ |
| FR-034 | T010, T020 | ✅ |
| NFR-001 | Exercised by the migrated ~20k-row book in `MoneyFileMigrationTests` | ✅ |
| NFR-002 | T013, T015, T025 | ✅ |
