# Tasks: Bringing a Microsoft Money book across

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — The Jet 4 / MSISAM reader

- [X] **T001** Open a file read-only, index data pages by owner, enumerate TDEFs, follow
  long-value chains, bound the file size
  - Implements: `src/MyFinance.Import/Mny/Jet/JetDatabase.cs`
  - Proven by: `MoneyReaderTests.The_file_opens_and_declares_tables`
  - Requirements: FR-001, FR-002, NFR-003
- [X] **T002** TDEF parsing and row decoding from both ends
  - Implements: `src/MyFinance.Import/Mny/Jet/{JetTable,JetColumn,JetRow}.cs`
  - Proven by: `MoneyReaderTests.Every_table_the_migration_reads_yields_exactly_the_row_count_it_declares`
    — the check that catches a misread table immediately — and
    `No_table_reads_short_except_the_one_known_overflow_row` for the rest of the file
  - ⚠️ One row per file is lost to an overflow page, on a table nothing reads. Recorded as a
    known defect in `spec.md`.
  - Requirement: FR-008
- [X] **T003** Value decoding: compressed and plain text, OLE dates, ten-thousandth currency
  - Implements: `src/MyFinance.Import/Mny/Jet/JetValues.cs`
  - Proven by: `JetValuesTests.Compressed_text_reads_one_byte_per_character`,
    `A_compressed_run_can_switch_back_to_two_byte_characters`, `Plain_utf16_text_reads`,
    `Empty_text_is_empty_rather_than_null`, `An_odd_byte_count_does_not_throw`,
    `A_date_counts_days_from_the_thirtieth_of_december_1899`, `A_zero_date_is_no_date`,
    `A_date_beyond_the_calendar_is_refused_rather_than_thrown`,
    `Currency_is_read_in_ten_thousandths`
  - Requirements: FR-005, FR-006, FR-007
- [X] **T004** Identify tables by column signature, refusing ambiguity
  - Implements: `src/MyFinance.Import/Mny/MoneyTables.cs`
  - Proven by: `MoneyReaderTests.The_core_tables_are_all_identifiable_by_their_columns`
  - Requirements: FR-003, FR-004
- [X] **T005** The Money-level model, deterministic and structurally sound
  - Implements: `src/MyFinance.Import/Mny/{MoneyReader,MoneyModels}.cs`
  - Proven by: `A_book_reads_with_accounts_categories_payees_and_transactions`,
    `Reading_the_same_file_twice_gives_the_same_answer`,
    `Every_transaction_belongs_to_an_account_that_exists`,
    `The_category_tree_has_no_cycles_and_every_parent_exists`,
    `Every_transfer_leg_names_the_account_at_the_other_end`,
    `The_parts_of_a_split_add_up_to_their_parent`, `Names_decode_as_readable_text`,
    `Dates_land_inside_the_years_a_person_could_have_kept_books`
  - Requirements: FR-009, NFR-002, NFR-004

## Phase 2 — Writing the book

- [X] **T006** Migrate into an empty book, in one transaction, replacing the seeded chart
  - Implements: `src/MyFinance.Data/Services/{MigrationService,MigrationModels}.cs`
  - Proven by: `MigrationServiceTests.An_account_and_its_transactions_come_across`,
    `Migrating_into_a_book_that_already_has_entries_is_refused`,
    `A_file_with_no_accounts_is_refused_rather_than_half_written`
  - Requirements: FR-011, FR-012
- [X] **T007** Three levels into two plus a kind
  - Proven by: `Money_three_level_categories_become_two_levels_plus_a_kind`,
    `Both_sides_of_the_category_tree_are_represented`, `A_transaction_keeps_its_category`
  - Requirement: FR-013
- [X] **T008** Payees folded on normalization, still pointed at; payee memory written
  - Proven by: `Payees_come_across_and_transactions_still_point_at_them`,
    `Payees_that_normalize_alike_are_folded_together`,
    `A_payee_remembers_the_category_it_was_last_filed_under`
  - Requirements: FR-014, FR-015
- [X] **T009** Splits preserved and summing; uncategorized still gets one
  - Proven by: `A_split_transaction_becomes_one_transaction_with_several_splits`,
    `An_uncategorized_transaction_still_gets_one_split`,
    `Every_migrated_transaction_has_splits_that_add_up_to_it`
  - Requirement: FR-017
- [X] **T010** Transfers linked and cancelling; stable order within a day
  - Proven by: `The_two_legs_of_a_transfer_are_linked_to_each_other`,
    `Both_legs_of_every_migrated_transfer_point_at_each_other`,
    `The_two_legs_of_every_transfer_cancel_each_other`,
    `Transactions_on_the_same_day_keep_a_stable_order`
  - Requirements: FR-018, FR-019
- [X] **T011** Options: closed accounts, projected bills
  - Proven by: `Closed_accounts_come_across_by_default`,
    `Closed_accounts_can_be_left_behind_on_request`,
    `Projected_bills_are_kept_by_default_and_can_be_left_out`
  - Requirements: FR-020, FR-021
- [X] **T012** [P] Merchant-code table brought across
  - Implements: `MigrationService`, `src/MyFinance.Core/Entities/MerchantCodeCategory.cs`
  - Proven by: exercised through `005-category-suggestion`'s SIC source tests
  - Requirement: FR-016
- [X] **T013** Balance-only accounts for types not modelled; no account numbers; bills counted
  and not converted
  - Proven by: `RegisterServiceTests.An_imported_balance_only_account_cannot_be_written_to`;
    FR-023 and FR-024 hold by construction — nothing in `MigrationService` writes either
  - Requirements: FR-022, FR-023, FR-024

## Phase 3 — Running it and checking it

- [X] **T014** Stage-by-stage progress, cancellable, rolling back completely
  - Implements: `MigrationService` with `IProgress<WorkProgress>`
  - Proven by: `ProgressAndCancellationTests.A_migration_reports_while_it_runs_rather_than_only_at_the_end`,
    `Stopping_a_migration_leaves_the_book_completely_untouched`,
    `A_migration_that_is_never_stopped_still_completes`
  - Requirements: FR-025, FR-026, NFR-001
- [X] **T015** The verification step: every account and its closing balance, against a real
  file
  - Implements: the migration wizard's final page
  - Proven by: `MoneyFileMigrationTests.A_real_money_file_migrates_and_every_balance_survives_the_journey`,
    `The_migrated_balance_matches_the_balance_money_reported`.
    **Needs Windows:** the wizard's layout only.
  - Requirement: FR-027
- [X] **T016** A password-protected file reported with its remedy
  - Implements: `MoneyReader` / `JetException`
  - Proven by: **not covered by a committed test** — no password-protected fixture exists,
    and creating one would require Money. The path is a single explicit check.
  - Requirement: FR-010

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001, FR-002 | T001 | ✅ |
| FR-003, FR-004 | T004 | ✅ |
| FR-005 – FR-007 | T003 | ✅ |
| FR-008 | T002 | ✅ |
| FR-009 | T005 | ✅ |
| FR-010 | T016 | ⚠️ **untested — no fixture exists** |
| FR-011, FR-012 | T006 | ✅ |
| FR-013 | T007 | ✅ |
| FR-014, FR-015 | T008 | ✅ |
| FR-016 | T012 | ✅ |
| FR-017 | T009 | ✅ |
| FR-018, FR-019 | T010 | ✅ |
| FR-020, FR-021 | T011 | ✅ |
| FR-022 – FR-024 | T013 | ✅ |
| FR-025, FR-026 | T014 | ✅ |
| FR-027 | T015 | ✅ |
| NFR-001 | T014 | ✅ |
| NFR-002 | By construction: the reader is `net10.0`, tested on Linux | ✅ |
| NFR-003 | T001 (512 MB cap) | ✅ |
| NFR-004 | Every committed test asserts structure only | ✅ |
