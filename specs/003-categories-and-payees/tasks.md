# Tasks: Categories and payees

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — The chart of categories

- [X] **T001** Seed a usable chart once, inside book creation
  - Implements: `src/MyFinance.Data/Services/DefaultCategories.cs`
  - Proven by: `CategoryServiceTests.A_new_book_starts_with_a_usable_chart_of_categories`,
    `Seeding_does_not_run_twice`
  - Requirement: FR-001
- [X] **T002** Two levels, enforced; a child takes its parent's kind; a heading's change of
  kind propagates
  - Implements: `src/MyFinance.Data/Services/CategoryService.cs`
  - Proven by: `Categories_stop_at_two_levels`, `A_child_takes_its_parents_kind`,
    `A_category_can_be_created_under_a_heading`,
    `Flipping_a_headings_kind_takes_its_children_with_it`, `Only_headings_are_offered_as_parents`
  - Requirements: FR-002, FR-003, FR-004, FR-007
- [X] **T003** Naming rules: unique within a heading, repeatable across headings; rename
  - Proven by: `The_same_name_can_appear_under_two_different_headings`,
    `Two_categories_under_one_heading_cannot_share_a_name`, `A_category_can_be_renamed`
  - Requirements: FR-005, FR-006
- [X] **T004** Delete only what was never used; archive and merge for everything else
  - Proven by: `An_unused_category_can_be_deleted`, `A_category_with_history_cannot_be_deleted`,
    `A_heading_with_subcategories_cannot_be_deleted`,
    `Archiving_hides_a_category_without_losing_its_history`,
    `Archiving_a_heading_archives_its_children`,
    `Merging_moves_the_transactions_and_retires_the_source`
  - Requirements: FR-008, FR-009, FR-010
- [X] **T005** Usage counts in the category list
  - Proven by: `The_category_list_reports_how_often_each_one_is_used`
  - Requirement: FR-011
- [X] **T006** Deleting a category a payee remembers leaves the payee usable
  - Proven by: `Deleting_a_category_a_payee_remembers_does_not_fail`
  - Requirement: FR-008
- [X] **T007** [P] Tax-related flag on the category
  - Implements: `src/MyFinance.Core/Entities/Category.cs` (`IsTaxRelated`)
  - Proven by: exercised through the reports suite (`ReportServiceTests`)
  - Requirement: FR-012

## Phase 2 — Payee normalization

- [X] **T008** The normalization function: case, punctuation, spacing; digits kept; nothing
  normalizes to empty
  - Implements: `src/MyFinance.Core/Payees/PayeeNormalizer.cs`
  - Proven by: `PayeeNormalizerTests` — `Case_punctuation_and_spacing_all_collapse`,
    `Nothing_normalizes_to_an_empty_string`,
    `Trailing_punctuation_does_not_leave_a_dangling_space`,
    `Equivalent_names_are_recognised_as_the_same_payee`,
    `Digits_are_kept_because_they_distinguish_real_payees`
  - Requirements: FR-013, FR-014, FR-015, NFR-001

## Phase 3 — The payee list

- [X] **T009** Find-or-create, blank refused, sorted names for autocomplete, clean miss
  - Implements: `src/MyFinance.Data/Services/PayeeService.cs`
  - Proven by: `PayeeServiceTests.A_payee_is_created_once_and_found_again`,
    `A_blank_payee_name_is_refused`, `Payee_names_come_back_sorted_for_autocomplete`,
    `Looking_up_a_payee_that_does_not_exist_returns_nothing`
  - Requirements: FR-016, FR-015, FR-021
- [X] **T010** Rename, refusing a rename onto an existing payee
  - Proven by: `A_payee_can_be_renamed`, `Renaming_onto_an_existing_payee_is_refused`
  - Requirement: FR-017
- [X] **T011** Delete only an unused payee
  - Proven by: `An_unused_payee_can_be_deleted`, `A_payee_with_transactions_cannot_be_deleted`
  - Requirement: FR-018
- [X] **T012** Merge: repoint transactions, keep the losing name as an alias, refuse a
  self-merge
  - Proven by: `Merging_repoints_the_transactions_and_keeps_the_old_name_as_an_alias`,
    `Merging_a_payee_into_itself_is_refused`
  - Requirement: FR-019
- [X] **T013** Payee memory — last category and last amount
  - Implements: `PayeeService`, written by `RegisterService`
  - Proven by: `RegisterServiceTests.A_payee_remembers_the_category_and_amount_last_used`,
    `A_split_transaction_teaches_the_payee_no_single_category`
  - Requirement: FR-020

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T001 | ✅ |
| FR-002 – FR-004, FR-007 | T002 | ✅ |
| FR-005, FR-006 | T003 | ✅ |
| FR-008 – FR-010 | T004, T006 | ✅ |
| FR-011 | T005 | ✅ |
| FR-012 | T007 | ✅ |
| FR-013 – FR-015 | T008, T009 | ✅ |
| FR-016, FR-021 | T009 | ✅ |
| FR-017 | T010 | ✅ |
| FR-018 | T011 | ✅ |
| FR-019 | T012 | ✅ |
| FR-020 | T013 | ✅ |
| NFR-001 | T008 | ✅ |
