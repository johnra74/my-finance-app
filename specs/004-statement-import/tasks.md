# Tasks: Importing a bank statement

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — Reading OFX

- [X] **T001** Format detection from contents, not extension
  - Implements: `src/MyFinance.Import/StatementFileReader.cs`
  - Proven by: `OfxStatementReaderTests.A_well_formed_file_that_is_not_ofx_is_refused`,
    `A_file_with_no_markup_at_all_is_refused`, `An_empty_file_is_refused`
  - Requirements: FR-001, FR-006
- [X] **T002** One parser for 1.x SGML and 2.x XML, tolerant of what banks actually emit
  - Implements: `src/MyFinance.Import/Ofx/OfxParser.cs`, `OfxNode.cs`
  - Proven by: `OfxParserTests.An_sgml_file_with_no_closing_tags_on_its_leaves_parses`,
    `Fields_of_an_sgml_transaction_are_siblings_not_nested`,
    `A_stray_closing_tag_is_ignored_rather_than_unwinding_the_document`,
    `An_empty_leaf_left_unclosed_does_not_swallow_the_fields_after_it`,
    `The_same_parser_reads_ofx_2_xml`, `Comments_and_self_closing_tags_are_skipped`,
    `Tag_names_are_matched_without_regard_to_case`, `Every_line_ending_convention_parses`,
    `A_header_with_no_blank_line_before_the_body_still_parses`,
    `Whitespace_inside_a_value_is_collapsed`, `A_bare_ampersand_survives_untouched`
  - Requirements: FR-002, FR-004
- [X] **T003** Encoding: declared charset honoured, BOM wins over a contradicting header,
  decoded once
  - Implements: `src/MyFinance.Import/Ofx/OfxEncodingSupport.cs`
  - Proven by: `OfxParserTests.Windows_1252_bytes_decode_when_the_header_declares_that_charset`,
    `A_utf8_byte_order_mark_wins_over_a_header_claiming_ascii`,
    `Decoding_happens_once_and_not_repeatedly`
  - Requirement: FR-003
- [X] **T004** Values: dates in every shape the specification allows, amounts, and a time
  zone recorded but never applied
  - Implements: `src/MyFinance.Import/Ofx/OfxValueParser.cs`
  - Proven by: `OfxValueParserTests.Every_shape_the_specification_allows_yields_the_same_date`,
    `The_stated_time_zone_is_recorded_but_never_applied`, `Ordinary_amounts_parse`,
    `A_leap_day_is_accepted_in_a_leap_year_and_refused_otherwise`,
    `A_currency_symbol_does_not_stop_an_amount_parsing`
  - Requirements: FR-008
- [X] **T005** Statements out of the node tree: several per file, statuses, account types,
  investment sections, truncation
  - Implements: `src/MyFinance.Import/Ofx/{OfxStatementReader,OfxStatementMapper}.cs`
  - Proven by: `OfxStatementReaderTests.Several_statements_in_one_file_are_all_returned`,
    `A_rejected_request_surfaces_the_banks_own_message`,
    `A_warning_status_does_not_stop_the_import`,
    `An_investment_statement_is_refused_rather_than_partly_read`,
    `Investment_sections_are_skipped_and_reported`, `A_truncated_file_yields_the_rows_it_did_contain`,
    `Account_types_map_onto_ours`, `An_unknown_account_type_maps_to_nothing_rather_than_guessing`,
    `A_credit_card_section_is_recognised`, `A_statement_totals_its_rows_as_stated`,
    `A_row_with_an_unreadable_date_is_skipped_and_reported`,
    `A_row_with_an_unreadable_amount_is_skipped_and_reported`,
    `Anything_unreadable_reports_failure_rather_than_throwing`
  - Requirements: FR-004, FR-005, FR-006, FR-007, FR-007a

## Phase 2 — Reading QIF

- [X] **T006** Whole-file convention scan: date order and decimal separator, with ambiguity
  and contradiction reported rather than guessed
  - Implements: `src/MyFinance.Import/Qif/QifConventions.cs`
  - Proven by: `QifParserTests.A_day_above_twelve_proves_the_order_is_day_first`,
    `A_month_above_twelve_in_second_place_proves_month_first`,
    `A_comma_is_a_decimal_point_not_a_thousands_separator`,
    `A_european_file_reads_its_comma_as_the_decimal_point`,
    `Grouped_amounts_with_a_full_stop_decimal_read_correctly`,
    `A_malformed_grouped_amount_reads_the_rightmost_separator_as_the_point`,
    `A_file_that_contradicts_itself_is_flagged`, `A_wholly_ambiguous_file_says_so`
  - Requirement: FR-009
- [X] **T007** Rows, splits, categories, cleared flags, cheque numbers, transfers as plain
  transactions
  - Implements: `src/MyFinance.Import/Qif/QifParser.cs`
  - Proven by: `QifParserTests.An_ordinary_export_reads`, `Splits_are_read_with_their_categories_and_memos`,
    `Splits_that_do_not_add_up_are_dropped_and_reported`, `The_cleared_flag_is_understood`,
    `A_cheque_number_is_carried_across`, `A_transfer_is_imported_as_a_plain_transaction_and_flagged`,
    `A_quicken_class_suffix_is_dropped`, `A_record_without_a_closing_caret_is_still_read`,
    `The_account_header_names_the_account_and_its_type`,
    `Every_shape_quicken_writes_a_date_in_parses`
  - Requirements: FR-010, FR-011
- [X] **T008** QIF into the book: the file's categories used or created on request, splits
  written as splits, transfers plain
  - Implements: `src/MyFinance.Data/Services/ImportService.cs`
  - Proven by: `QifImportTests.A_qif_export_loads_into_the_register`,
    `The_files_own_categories_are_used_when_the_book_already_has_them`,
    `A_category_the_book_lacks_is_reported_rather_than_invented`,
    `Missing_categories_are_created_when_the_user_asks`,
    `Creating_a_category_reuses_a_heading_that_already_exists`,
    `A_split_whose_categories_the_book_lacks_can_still_be_brought_across`,
    `Split_lines_in_the_file_arrive_as_a_split_transaction`,
    `A_transfer_marked_in_the_file_arrives_as_a_plain_transaction`,
    `Every_category_path_referenced_is_reported`, `The_category_list_can_be_read_separately`,
    `Re_importing_a_qif_file_flags_its_rows_as_probable_duplicates`
  - Requirements: FR-010, FR-011

## Phase 3 — Signs

- [X] **T009** Balance-first sign analysis, abstaining where the evidence is thin, never
  inverting on a heuristic alone
  - Implements: `src/MyFinance.Import/Model/SignConventionAnalyzer.cs`
  - Proven by: `SignConventionAnalyzerTests.A_card_statement_that_balances_as_written_is_certain`,
    `A_card_statement_whose_amounts_are_reversed_is_caught_by_the_balance`,
    `A_reversed_card_statement_is_detected_from_its_ledger_balance`,
    `A_correctly_signed_statement_is_left_alone`,
    `A_bank_statement_is_never_inverted_on_a_heuristic_alone`,
    `A_bank_statement_is_still_inverted_when_the_balance_proves_it`,
    `On_a_new_account_the_balance_proves_nothing_so_the_types_decide`,
    `Too_few_typed_rows_and_the_check_abstains`, `Untyped_rows_are_not_evidence_either_way`,
    `Transaction_types_agreeing_with_the_amounts_reads_as_correct`,
    `A_balance_that_matches_neither_reading_is_reported_without_blocking`,
    `A_ledger_stated_the_other_way_round_is_recognised_without_inverting_the_rows`,
    `Reversing_the_signs_flips_the_whole_statement`, `Both_projections_are_offered_so_the_user_can_see_the_choice`
  - Requirements: FR-018, FR-019, FR-020

## Phase 4 — Payees

- [X] **T010** Descriptor cleaning: prefixes, branch numbers, state codes, references,
  recasing — without eating parts that identify the merchant
  - Implements: `src/MyFinance.Import/Payees/{DescriptorCleaner,DescriptorRules}.cs`
  - Proven by: `DescriptorCleanerTests.Processor_and_bank_prefixes_come_off`,
    `A_branch_number_is_stripped_so_two_branches_become_one_payee`,
    `A_trailing_state_code_comes_off_but_the_city_stays`,
    `A_merchant_ending_in_two_letters_that_is_not_a_state_survives`,
    `A_number_that_is_part_of_the_name_is_never_stripped`,
    `An_out_of_range_numeric_reference_is_left_alone`,
    `An_unterminated_reference_does_not_swallow_the_rest_of_the_name`,
    `Recognised_references_are_expanded`, `Acronyms_and_connectives_survive_recasing`,
    `A_name_the_bank_already_formatted_is_left_alone`,
    `The_city_stripped_reading_is_offered_as_an_alternative`,
    `An_empty_descriptor_is_handled_without_throwing`
  - Requirement: FR-021
- [X] **T011** A stable key over the unchanging parts of a descriptor
  - Implements: `DescriptorCleaner`
  - Proven by: `DescriptorCleanerTests.Two_visits_to_the_same_merchant_produce_the_same_key`,
    `Genuinely_different_merchants_get_different_keys`,
    `Volatile_trailing_parts_do_not_change_the_key`,
    `The_key_keeps_the_stable_parts_of_the_descriptor`,
    `The_key_is_case_and_punctuation_insensitive`,
    `Distinct_locations_of_one_brand_stay_distinct`
  - Requirement: FR-023
- [X] **T012** One payee per merchant per file; raw descriptor always kept; corrections
  honoured next time
  - Implements: `src/MyFinance.Import/Payees/PayeeMatcher.cs`, `ImportService`
  - Proven by: `ImportServiceTests.One_payee_is_created_for_a_merchant_appearing_many_times`,
    `The_raw_bank_descriptor_is_kept_in_the_memo`, `The_raw_descriptor_is_always_preserved`,
    `A_correction_is_remembered_and_honoured_by_the_next_import`,
    `One_merchant_keeps_one_stand_in_throughout`
  - Requirements: FR-022, FR-023, FR-024

## Phase 5 — Duplicates

- [X] **T013** Reference-first duplicate detection, with an evidence fallback that suspects
  rather than drops
  - Implements: `src/MyFinance.Import/Dedupe/DuplicateDetector.cs`
  - Proven by: `DuplicateDetectorTests.A_matching_bank_reference_is_certain_and_cannot_be_overridden`,
    `An_unseen_reference_is_new_even_when_the_amount_and_date_match`,
    `A_row_the_register_has_never_seen_is_new`, `A_different_amount_is_never_a_duplicate`,
    `A_row_with_no_reference_matching_on_date_amount_and_payee_is_likely`,
    `A_near_match_whose_payee_disagrees_is_only_possible_and_imports_by_default`,
    `The_date_tolerance_has_an_edge`, `Three_identical_amounts_in_a_week_do_not_collapse_into_one`,
    `A_reference_repeated_inside_one_file_is_kept_only_once`,
    `A_duplicated_amount_line_is_not_counted_twice`
  - Requirements: FR-015, FR-016, FR-017

## Phase 6 — Matching the account and writing

- [X] **T014** Account recognition by keyed digest, never by account number
  - Implements: `src/MyFinance.Data/Services/OfxAccountKey.cs`,
    `SettingsService.GetOrCreateOfxSecretAsync`
  - Proven by: `ImportServiceTests.An_account_is_recognised_by_the_next_statement_from_the_same_bank`,
    `Linking_stores_a_digest_and_never_the_account_number`
  - Requirements: FR-012, FR-013
- [X] **T015** Write the selection, in one transaction, with batch, references, cleared state
  and sequencing
  - Implements: `src/MyFinance.Data/Services/ImportService.cs`
  - Proven by: `ImportServiceTests.A_statement_loads_into_the_register`,
    `Transaction_fields_survive_the_journey`, `Every_imported_row_carries_the_banks_reference_and_its_batch`,
    `Rows_on_one_date_get_distinct_increasing_sequences`,
    `The_cleared_state_from_the_file_is_honoured_row_by_row`,
    `A_missing_cleared_flag_leaves_the_state_unstated`,
    `The_resulting_balance_matches_what_the_statement_says`,
    `An_unreadable_row_is_skipped_and_the_rest_import`, `The_import_history_records_what_happened`
  - Requirements: FR-025, FR-028, FR-029
- [X] **T016** Exclusions honoured; an empty selection refused; a balance-only account refused
  - Proven by: `ImportServiceTests.Excluded_rows_are_not_written`,
    `Selecting_nothing_is_refused_rather_than_writing_an_empty_batch`,
    `Importing_into_a_balance_only_account_is_refused`
  - Requirements: FR-030, FR-031, FR-014
- [X] **T017** Undo: exactly its rows, the balance restored, refused after reconciliation or
  a second time, keeping what it learned
  - Proven by: `ImportServiceTests.Undoing_an_import_removes_exactly_its_rows_and_restores_the_balance`,
    `Undoing_is_refused_once_a_row_has_been_reconciled`,
    `Undoing_the_same_import_twice_is_refused`, `Undoing_keeps_the_payees_and_the_mappings_it_learned`
  - Requirements: FR-025, FR-026, FR-027

## Phase 7 — Staying usable while it works

- [X] **T018** Progress and cancellation through every stage, rolling back on stop
  - Implements: `src/MyFinance.Core/Progress/WorkProgress.cs`,
    `src/MyFinance.App/ViewModels/BusyViewModel.cs`,
    `src/MyFinance.App/Views/Controls/BusyOverlay.xaml`
  - Proven by: `ProgressAndCancellationTests.Counted_progress_never_goes_backwards_and_finishes_complete`,
    `Progress_describes_itself_honestly`, `A_migration_reports_while_it_runs_rather_than_only_at_the_end`,
    `Stopping_a_migration_leaves_the_book_completely_untouched`,
    `A_migration_that_is_never_stopped_still_completes`, `Stopping_a_backup_leaves_no_half_written_file`
  - Requirements: NFR-002, NFR-003

## Phase 8 — Fixtures and redaction

- [X] **T019** A redactor, and golden tests over redacted real statements
  - Implements: `tests/MyFinance.Import.Tests/Fixtures/OfxRedactor.cs`
  - Proven by: `FixtureTests.Every_redacted_statement_parses`,
    `Every_redacted_statement_has_usable_transactions`,
    `Redacted_statements_still_reconcile_against_their_stated_balance`,
    `Redaction_removes_the_account_number_and_the_merchant`,
    `Redaction_leaves_the_files_shape_untouched`, `Shifting_dates_keeps_their_spacing`,
    `An_amount_that_needs_no_rounding_is_not_flagged`, `Extra_precision_is_rounded_and_reported`
  - Requirement: constitution principle 8

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001, FR-006 | T001, T005 | ✅ |
| FR-002, FR-004 | T002, T005 | ✅ |
| FR-003 | T003 | ✅ |
| FR-005, FR-007, FR-007a | T005 | ✅ |
| FR-008 | T004 | ✅ |
| FR-009 | T006 | ✅ |
| FR-010, FR-011 | T007, T008 | ✅ |
| FR-012, FR-013 | T014 | ✅ |
| FR-014 | T016 | ✅ |
| FR-015 – FR-017 | T013 | ✅ |
| FR-018 – FR-020 | T009 | ✅ |
| FR-021 – FR-024 | T010, T011, T012 | ✅ |
| FR-025 – FR-027 | T015, T017 | ✅ |
| FR-028, FR-029 | T015 | ✅ |
| FR-030, FR-031 | T016 | ✅ |
| NFR-001 | By construction: `MyFinance.Import` has no EF reference | ✅ |
| NFR-002, NFR-003 | T018 | ✅ |
| NFR-004 | By construction: no network code on this path | ✅ |
