# Tasks: Recommending a category

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — Rules

- [X] **T001** Rule matching: payee, description, amount, combined; comparison kinds; case;
  account restriction; signed amounts
  - Implements: `src/MyFinance.Import/Categorization/RuleEvaluator.cs` (`RuleSpec`, `RuleEvaluator`)
  - Proven by: `RuleEvaluatorTests.A_payee_rule_fires_on_the_payee`,
    `A_memo_rule_looks_at_the_descriptor_not_the_payee`, `A_combined_rule_looks_at_either`,
    `Every_comparison_kind_works`, `Matching_ignores_case_unless_told_otherwise`,
    `Amount_conditions_compare_against_the_signed_amount`,
    `An_account_restricted_rule_only_fires_on_that_account`,
    `A_rule_matching_nothing_falls_through`, `Nothing_matching_returns_nothing`
  - Requirement: FR-008
- [X] **T002** Order and tie-breaking; disabled rules excluded; payee-only rules still win
  - Proven by: `RuleEvaluatorTests.The_first_matching_rule_wins_in_priority_order`,
    `Rules_of_equal_priority_break_the_tie_on_age`, `Ties_resolve_the_same_way_every_time`,
    `A_disabled_rule_never_fires`, `Disabled_rules_are_left_out_of_the_evaluation_order`,
    `A_rule_may_rewrite_the_payee_without_naming_a_category`,
    `A_rule_that_only_rewrites_the_payee_still_wins`,
    `A_rule_that_would_consume_the_whole_name_does_not_fire`
  - Requirements: FR-009, FR-010
- [X] **T003** Rule storage: create, read back, disable, reorder, delete, validate, try before
  saving, count usage
  - Implements: `src/MyFinance.Data/Services/CategorizationRuleService.cs`
  - Proven by: `CategorizationRuleServiceTests.A_rule_can_be_created_and_read_back`,
    `A_rule_can_be_disabled_without_losing_it`, `A_rule_can_be_deleted`, `Rules_can_be_reordered`,
    `New_rules_go_to_the_end_of_the_order`,
    `The_specs_handed_to_the_evaluator_come_back_in_priority_order`,
    `A_nameless_rule_is_refused`, `A_rule_that_does_nothing_is_refused`,
    `An_invalid_regular_expression_is_refused_at_the_point_of_saving`,
    `Patterns_are_checked_before_they_are_saved`,
    `A_rule_can_be_tried_against_the_existing_register_before_it_is_saved`,
    `A_rule_restricted_to_one_account_says_so`, `An_amount_rule_reads_as_plain_english`,
    `An_amount_rule_reads_money_out_as_negative`
  - Requirements: FR-011, FR-012, FR-013
- [X] **T004** A broken stored expression degrades rather than stopping an import
  - Proven by: `A_broken_regular_expression_does_not_stop_the_import`,
    `Trying_a_broken_expression_reports_nothing_rather_than_throwing`
  - Requirement: FR-012
- [X] **T005** Applied rules reported and counted
  - Proven by: `ImportServiceTests.Applied_rules_are_reported_so_a_surprise_can_be_explained`,
    `A_rule_that_fires_is_counted_on_the_rules_screen`,
    `A_rule_restricted_to_another_account_does_not_fire`
  - Requirement: FR-014

## Phase 2 — The classifier

- [X] **T006** Tokenizer: lower-cased, split on punctuation, stop words and store numbers
  dropped, digits inside words kept
  - Implements: `CategoryClassifier.TextTokenizer`
  - Proven by: `CategoryClassifierTests.Text_is_lower_cased_and_split_on_punctuation`,
    `Single_characters_and_common_words_carry_no_signal`,
    `Store_numbers_are_dropped_rather_than_learned_as_noise`,
    `A_word_with_digits_in_it_survives`, `Nothing_meaningful_yields_no_tokens`
  - Requirement: FR-018
- [X] **T007** Naive Bayes in log space, with Laplace smoothing and every abstention rule
  - Implements: `src/MyFinance.Import/Categorization/CategoryClassifier.cs`
  - Proven by: `CategoryClassifierTests.A_familiar_merchant_is_recognised`,
    `Different_merchants_land_in_different_categories`,
    `An_untrained_classifier_never_suggests_anything`,
    `A_book_with_too_little_history_is_left_alone`,
    `A_category_with_too_few_examples_is_not_offered`,
    `A_merchant_never_seen_before_produces_nothing_confident`,
    `The_confidence_threshold_is_respected`, `Confidence_stays_a_probability`,
    `A_long_descriptor_does_not_underflow_to_a_meaningless_score`,
    `Examples_with_no_usable_words_are_not_counted`,
    `The_reported_support_is_the_number_of_examples_behind_the_answer`,
    `Nothing_known_suggests_nothing`
  - Requirements: FR-016, FR-017
- [X] **T008** Trained from the user's own history, excluding transfers and voided rows
  - Implements: `SuggestionService.TrainClassifierAsync` (moved here from `ImportService`
    once there were two callers)
  - Proven by: `ImportServiceTests.Transfers_and_voided_rows_are_kept_out_of_the_training_data`,
    `With_enough_history_an_unknown_payee_gets_a_statistical_suggestion`
  - Requirements: FR-015, FR-019

## Phase 3 — Merchant resemblance

- [X] **T009** The embedder: local, deterministic, unit vectors, never throws on load
  - Implements: `src/MyFinance.Semantics/OnnxTextEmbedder.cs`
  - Proven by: `OnnxTextEmbedderTests.The_model_loads`,
    `A_vector_is_the_right_width_finite_and_of_unit_length`,
    `The_same_text_embeds_the_same_way_every_time`,
    `Padding_in_a_batch_does_not_change_a_short_text`,
    `A_batch_comes_back_in_the_order_it_was_given`,
    `Merchants_of_a_kind_sit_closer_than_merchants_of_different_kinds`,
    `Blank_text_still_produces_a_usable_vector`, `Empty_input_is_answered_with_nothing_rather_than_a_throw`
  - Requirements: FR-021, NFR-002
- [X] **T010** The optional seam
  - Implements: `src/MyFinance.Import/Categorization/ITextEmbedder.cs` (+ `NullTextEmbedder`)
  - Proven by: `SimilarityIndexTests.The_null_embedder_is_unavailable_and_answers_nothing`,
    `With_no_vector_the_chain_is_unchanged`
  - Requirement: FR-022
- [X] **T011** The similarity index: floor, agreement, weighting from the floor, naming the
  match
  - Implements: `src/MyFinance.Import/Categorization/SimilarityIndex.cs`
  - Proven by: `SimilarityIndexTests.The_closest_entry_comes_first`,
    `Nothing_below_the_floor_is_returned`,
    `The_default_floor_rejects_the_distance_unrelated_merchants_sit_at`,
    `Neighbours_agreeing_on_a_category_raise_the_confidence`,
    `A_neighbourhood_that_cannot_agree_is_not_reported`,
    `A_near_exact_match_beats_a_crowd_of_vague_ones`,
    `A_distant_merchant_is_not_reported_at_all`, `The_prediction_names_the_merchant_it_matched`,
    `Only_the_requested_number_of_neighbours_come_back`, `An_empty_index_says_nothing`,
    `Vectors_of_a_different_width_are_ignored_rather_than_crashing`
  - Requirements: FR-020, FR-023, FR-024, FR-025
- [X] **T012** [P] int8 vector quantization for the cache
  - Implements: `src/MyFinance.Import/Categorization/VectorQuantizer.cs`
  - Proven by: `VectorQuantizerTests.A_vector_survives_the_round_trip_intact_enough_to_rank_with`,
    `Relative_distances_are_preserved`, `Packing_costs_one_byte_a_dimension_plus_a_small_header`,
    `A_zero_vector_does_not_divide_by_its_own_length`,
    `An_empty_or_missing_vector_round_trips_to_nothing`
  - Requirement: FR-026
- [X] **T013** The vector cache, keyed on payee, text hash and model
  - Implements: `src/MyFinance.Data/Services/PayeeEmbeddingService.cs`
  - Proven by: exercised through `SuggestionServiceTests`
  - Requirement: FR-026

## Phase 4 — The chain

- [X] **T014** Six sources in a fixed order of authority, each describing itself
  - Implements: `src/MyFinance.Import/Categorization/CategorySuggester.cs`
  - Proven by: `A_rule_outranks_everything_else`, `The_files_own_category_beats_payee_memory`,
    `Payee_memory_beats_the_statistical_guess`, `The_statistical_guess_is_the_last_resort`,
    `What_the_payee_was_last_filed_under_beats_a_lookalike`,
    `A_close_merchant_supplies_a_category_when_nothing_else_can`,
    `A_lookalike_from_the_users_own_history_beats_a_generic_industry_code`,
    `A_merchant_code_is_used_when_nothing_else_answers`,
    `An_unknown_merchant_code_is_not_invented_into_a_category`,
    `The_similarity_suggestion_says_which_merchant_it_matched`,
    `Every_source_describes_itself_in_words_a_person_can_read`
  - Requirements: FR-001, FR-004, FR-027, FR-028
- [X] **T015** Certainty as a property of the suggestion
  - Implements: `CategorySuggestion.IsCertain`
  - Proven by: `What_the_user_decided_can_be_applied_without_asking`,
    `What_was_inferred_is_only_ever_offered`, `Nothing_suggested_is_not_a_certainty_either`
  - Requirements: FR-002, FR-003

## Phase 5 — One cached context, two callers

- [X] **T016** `SuggestionService`: build the context once, reuse it, rebuild on a
  fingerprint change
  - Implements: `src/MyFinance.Data/Services/SuggestionService.cs`
  - Proven by: `SuggestionServiceTests.The_model_is_built_once_and_reused`,
    `The_model_is_rebuilt_after_a_transaction_is_added`,
    `The_model_is_rebuilt_after_a_transaction_is_recategorized`,
    `The_model_is_rebuilt_after_a_rule_is_written`
  - Requirements: FR-029, FR-030
- [X] **T017** The import preview as the service's second caller — no behaviour change
  - Implements: `ImportService.PrepareAsync`
  - Proven by: the 24 pre-existing import tests passing untouched, which was the stated
    success signal for the move
  - Requirement: FR-006
- [X] **T018** The transaction editor as the first caller: the full chain, off the interface
  thread, only when the category is blank
  - Implements: `src/MyFinance.App/ViewModels/Dialogs/TransactionEditorViewModel.cs`
    (`SuggestCategoryAsync`, `UseSuggestionCommand`, `ClearSuggestion`)
  - Proven by: `SuggestionServiceTests.A_payee_filed_before_is_recommended_the_same_way_again`,
    `An_unknown_payee_with_familiar_words_is_a_guess_not_a_certainty`,
    `A_rule_beats_what_the_payee_was_last_filed_under`,
    `A_new_book_with_nothing_to_learn_from_invents_nothing`,
    `An_empty_payee_is_not_worth_asking_about`.
    **Needs Windows:** that the offer line renders and the "Use this" link fills the box.
  - Requirements: FR-006, FR-007, NFR-001
- [X] **T019** Nothing invented on a book with no history
  - Proven by: `ImportServiceTests.A_new_book_gets_no_statistical_guesses`,
    `An_unrecognised_payee_lands_uncategorized_for_the_worklist`,
    `A_known_payee_pre_fills_its_remembered_category`, `Payee_memory_still_beats_the_statistical_model`
  - Requirement: FR-005

## Phase 6 — Measuring it

- [X] **T020** A date-based holdout benchmark on a real book, reported rather than assumed
  - Implements: `tests/MyFinance.Semantics.Tests/CategorySuggestionBenchmark.cs`
  - Proven by: it *is* the measurement. Produces the figures in SC-009.
  - Requirement: SC-009

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001, FR-004 | T014 | ✅ |
| FR-002, FR-003 | T015 | ✅ |
| FR-005 | T019 | ✅ |
| FR-006 | T017, T018 | ✅ |
| FR-007 | T018 | ⚠️ logic tested; layout needs Windows |
| FR-008 – FR-010 | T001, T002 | ✅ |
| FR-011 – FR-013 | T003, T004 | ✅ |
| FR-014 | T005 | ✅ |
| FR-015, FR-019 | T008 | ✅ |
| FR-016 – FR-018 | T006, T007 | ✅ |
| FR-020, FR-023 – FR-025 | T011 | ✅ |
| FR-021 | T009 | ✅ |
| FR-022 | T010 | ✅ |
| FR-026 | T012, T013 | ✅ |
| FR-027, FR-028 | T014 | ✅ |
| FR-029, FR-030 | T016 | ✅ |
| NFR-001 | T018 | ✅ |
| NFR-002 | T009 | ✅ |
| NFR-003 | By construction: `MyFinance.Semantics` makes no network call | ✅ |
| NFR-004 | ~22 MB measured in the published output | ✅ |
