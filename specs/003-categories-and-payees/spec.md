# Feature Specification: Categories and payees

**Folder:** `003-categories-and-payees`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "The two-level chart of accounts Money used, and a payee list that does not fill
up with fifty spellings of the same shop."

## User Scenarios & Testing

### Primary user story

Reports are only as good as the classification behind them. The user needs a chart of
categories they can shape to how they actually think about their money, and a payee list
that stays a list of *shops* rather than a list of *bank descriptors*. Both accumulate
mistakes over decades — duplicates, categories that turned out to be the same thing — so
both need a way to be tidied without destroying the history filed under them.

### Acceptance scenarios

1. **Given** a new book, **When** it is first opened, **Then** it already has a usable chart
   of categories, so nobody has to invent one before entering a transaction.
2. **Given** a heading, **When** a subcategory is added, **Then** it takes the heading's
   income-or-expense kind, and displays as `Heading : Subcategory`.
3. **Given** a category with transactions filed under it, **When** the user tries to delete
   it, **Then** it is refused, and archiving or merging is offered instead.
4. **Given** two categories that turned out to mean the same thing, **When** one is merged
   into the other, **Then** every transaction moves and the source is retired.
5. **Given** two payees that are the same shop, **When** they are merged, **Then** the
   transactions move and the losing name is kept as an alias, so a future import lands on the
   survivor.
6. **Given** the descriptors `WALMART #1234` and `Wal-Mart`, **When** they are normalized,
   **Then** they are recognised as the same payee.

### Edge cases

- Same name under two different headings (`Bills : Insurance`, `Auto : Insurance`) → allowed;
  the pair is what identifies a category, not the leaf name.
- Two categories with the same name under *one* heading → refused.
- Deleting a heading that has children → refused.
- Flipping a heading from expense to income → its children follow, so no child ever
  contradicts its parent.
- Deleting a category that a payee remembers as its usual one → succeeds; the payee simply
  forgets.
- A descriptor that normalizes to nothing (all punctuation) → refused, rather than creating a
  payee with an empty key that everything then matches.
- Merging a payee into itself → refused.

## Requirements

### Functional requirements

**Categories**

- **FR-001**: The system MUST seed a new book with a usable chart of categories, once and
  only once.
- **FR-002**: The system MUST support exactly two levels — a heading and its subcategories —
  and MUST refuse a third.
- **FR-003**: The system MUST classify every category as income or expense, and a child MUST
  take its parent's kind.
- **FR-004**: The system MUST propagate a heading's change of kind to its children.
- **FR-005**: The system MUST allow the same leaf name under two different headings, and MUST
  refuse two identical names under one heading.
- **FR-006**: The system MUST support renaming and re-parenting.
- **FR-007**: The system MUST offer only headings as parents.
- **FR-008**: The system MUST refuse to delete a category that has been used, or a heading
  that has children.
- **FR-009**: The system MUST support archiving, which hides a category from pickers while
  leaving every historical transaction resolvable, and MUST archive a heading's children with
  it.
- **FR-010**: The system MUST support merging one category into another, moving every
  transaction and retiring the source.
- **FR-011**: The system MUST report how often each category is used, so the user can see
  what is worth tidying.
- **FR-012**: The system MUST mark categories as tax-related, for the tax-related
  transactions report.

**Payees**

- **FR-013**: The system MUST keep a normalized form of every payee name — case,
  punctuation and spacing collapsed — used to match noisy bank descriptors back to a payee.
- **FR-014**: The system MUST keep digits in the normalized form, because they distinguish
  genuinely different payees.
- **FR-015**: The system MUST NOT allow a name that normalizes to nothing.
- **FR-016**: The system MUST create a payee once and find it again on the next occurrence.
- **FR-017**: The system MUST support renaming, and MUST refuse a rename onto an existing
  payee — merge is the operation for that.
- **FR-018**: The system MUST refuse to delete a payee with transactions.
- **FR-019**: The system MUST support merging payees, repointing the transactions and keeping
  the losing name as an **alias**, so a future import lands on the survivor.
- **FR-020**: The system MUST remember, per payee, the category and amount last used.
- **FR-021**: The system MUST return payee names sorted, for autocomplete.

**Editing**

- **FR-022**: The category and payee lists are **views, not editors**. Every change MUST go
  through the editor dialog or an explicit command, and thence through a service — never
  through an in-place edit of a grid cell. The lists show a projection built for display; a
  cell that writes back either fails against a value with no setter or, worse, succeeds
  against a detached copy and discards the change without saying so. Both happened.

### Non-functional requirements

- **NFR-001**: Normalization MUST be deterministic and stable across releases. Changing it
  silently re-partitions every payee in the book.

## Key Entities

- **Category** — leaf name, optional parent, income-or-expense kind, tax-related flag,
  archived flag, sort order. Displays as `Parent : Child`.
- **Payee** — display name, normalized name, last category, last amount, active flag, notes.
- **Payee alias** — a normalized raw descriptor that resolves to a payee. What makes a
  correction made once stick for every later download.

## Success Criteria

- **SC-001**: A new book opens with categories already in it, and re-opening does not seed
  again. *`CategoryServiceTests.A_new_book_starts_with_a_usable_chart_of_categories`,
  `Seeding_does_not_run_twice`.*
- **SC-002**: A third level is refused. *`Categories_stop_at_two_levels`.*
- **SC-003**: A category with history cannot be deleted, but can be archived or merged, and
  its history still resolves. *`A_category_with_history_cannot_be_deleted`,
  `Archiving_hides_a_category_without_losing_its_history`,
  `Merging_moves_the_transactions_and_retires_the_source`.*
- **SC-004**: Changing a heading's kind leaves no child contradicting its parent.
  *`Flipping_a_headings_kind_takes_its_children_with_it`.*
- **SC-005**: `WALMART #1234`, `Wal-Mart` and `wal mart` resolve to one payee, while payees
  distinguished only by digits stay distinct. *`PayeeNormalizerTests.Equivalent_names_are_recognised_as_the_same_payee`,
  `Digits_are_kept_because_they_distinguish_real_payees`,
  `Case_punctuation_and_spacing_all_collapse`.*
- **SC-006**: After merging two payees, the losing name still finds the survivor on a later
  import. *`PayeeServiceTests.Merging_repoints_the_transactions_and_keeps_the_old_name_as_an_alias`.*
- **SC-007**: Deleting a category a payee remembers does not fail.
  *`CategoryServiceTests.Deleting_a_category_a_payee_remembers_does_not_fail`.*
- **SC-008**: No check-box column in the interface can write back into a list projection.
  *`GridBindingTests.Every_check_box_column_declares_whether_it_can_be_edited`,
  `GridBindingTests.The_categories_grid_binds_its_check_boxes_one_way`,
  `GridBindingTests.The_category_list_item_is_a_read_model`.*

## Assumptions

- Depends on constitution principles **3** (writes through a service) and **4** (normalization
  is pure and lives in `MyFinance.Core`).
- Two levels is enough, because it is what the reference application used and what its
  three-level tree collapses to once the two fixed roots become a flag. See
  `009-money-migration`.
- Category identity is (parent, leaf name). Renaming a category does not re-file anything.

## Out of Scope

- A tax-line assignment scheme mapping categories to specific tax form lines. Only a
  boolean flag exists today; see the gap table in `specs/README.md`.
- Category budgets — see `007-budgets`.
- Rules that assign a category automatically — see `005-category-suggestion`.
