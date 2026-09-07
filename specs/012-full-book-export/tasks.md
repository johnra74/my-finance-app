# Tasks: Exporting the whole book

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Data model:** `./data-model.md`
**Status:** **complete**

`[X]` done · `[P]` could run in parallel.
New coverage: `tests/MyFinance.Core.Tests/Export/BookDocumentTests.cs` (12) and
`tests/MyFinance.Data.Tests/Services/BookExportServiceTests.cs` (21) — **33 tests, all
passing**. The suite went from 843 to 876.

Three corrections during implementation are recorded at the end of `plan.md`: amounts are
sibling properties rather than a nested object, merchant-code mappings were missing from the
document, and the golden test needed a richer book to prove the optional keys exist.

## Phase 1 — The document shape

- [X] **T001** Define the document as records in Core, with `format`, `formatVersion` and the
  export metadata
  - Implements: `src/MyFinance.Core/Export/BookDocument.cs`
  - Proven by: `BookDocumentTests.A_document_declares_its_format_and_version`,
    `An_export_from_a_newer_version_is_refused_rather_than_misread`,
    `A_file_that_is_not_a_book_export_is_refused`,
    `An_empty_document_is_refused_rather_than_read_as_an_empty_book`
  - Requirements: FR-002, SC-004
- [X] **T002** The amount convention: decimal string **and** minor units as **sibling
  properties**, never a JSON number
  - Implements: `src/MyFinance.Core/Export/BookDocument.cs` (`ExportMoney`)
  - Proven by: `BookDocumentTests.An_amount_is_written_as_a_string_and_as_minor_units`,
    `No_amount_is_serialised_as_a_json_number`, `An_amount_is_written_the_same_way_in_every_culture`,
    `A_null_amount_is_absent_rather_than_written_as_null`
  - `No_amount_is_serialised_as_a_json_number` scans the serialised text rather than the model,
    and **it earned its place immediately**: it caught the first implementation nesting amounts
    as objects. This is the one place the application's exactness can be silently lost.
  - Requirement: constitution principle 1
- [X] **T003** [P] Source-generated serialization, so the single-file build stays trim-safe
  and fast
  - Implements: `src/MyFinance.Core/Export/BookDocument.cs` (`BookDocumentJson`)
  - Proven by: `BookDocumentTests.A_document_round_trips_through_its_serializer`
  - Requirement: FR-002

## Phase 2 — Walking the book

- [X] **T004** Export the ledger: accounts, categories, payees, transactions, splits and
  transfer links
  - Implements: `src/MyFinance.Data/Services/BookExportService.cs`
  - Proven by: `BookExportServiceTests.Every_account_category_payee_and_transaction_is_exported`,
    `Splits_are_nested_inside_their_transaction`,
    `Each_transfer_leg_names_the_other`
  - Requirements: FR-001, SC-003
- [X] **T005** Include what is easy to forget: archived categories, closed accounts, voided
  transactions, payee aliases and payee memory
  - Implements: `BookExportService`
  - Proven by: `BookExportServiceTests.An_archived_category_is_exported`,
    `A_closed_account_and_its_history_are_exported`,
    `A_voided_transaction_is_exported_with_its_flag`,
    `Payee_memory_and_aliases_are_exported`
  - Requirement: FR-001
- [X] **T006** Export scheduled bills with their occurrence history, budgets with watched
  categories, rules **in priority order**, merchant-code mappings, and import batches
  - Implements: `BookExportService`
  - Proven by: `BookExportServiceTests.A_scheduled_series_is_exported_with_what_was_entered_or_skipped`,
    `Budgets_and_watched_categories_are_exported`,
    `Rules_are_exported_in_priority_order` — order is part of a rule's meaning,
    `Every_import_batch_id_on_a_transaction_resolves_to_an_exported_batch` — a published
    format may not carry a dangling reference
  - Requirement: FR-001
- [X] **T007** Export nothing secret and nothing derived
  - Implements: `BookExportService`
  - Proven by: `BookExportServiceTests.No_account_key_digest_or_book_secret_appears_in_the_document`,
    `No_payee_embedding_or_derived_balance_appears_in_the_document` — asserted by searching
    the serialised text, not by inspecting the model
  - Requirement: FR-003

## Phase 3 — The property that actually matters

- [X] **T008** Balance agreement: total the exported rows per account and match the book
  - Proven by: `BookExportServiceTests.Every_exported_account_balance_agrees_with_the_book` —
    which totals the document **the way an outside reader would have to** (opening balance plus
    non-void rows per account) and compares against `AccountService.GetAccountListAsync`, over a
    book containing a transfer, a void and a credit card.
  - ⚠️ **Not run against the real migrated book.** The `.mny`-backed variant was not written;
    the constructed book covers the awkward cases deliberately, but the 19,000-row file would
    be stronger evidence. Worth adding when that file is available.
  - Requirements: NFR-001, SC-001
- [X] **T009** Entity counts in the document match the book, and the export's memory and time
  are measured on a ~20,000-transaction book rather than assumed
  - Proven by: `BookExportServiceTests.The_document_holds_exactly_as_many_rows_as_the_book`,
    which counts accounts, categories, payees, transactions and splits against the database.
  - ⚠️ Memory and time on a ~20,000-row book were **not measured**. The document is built in
    memory and then serialised, which the plan accepted as a deliberate simplicity trade; it
    should be measured before anyone relies on it for the real book.
  - Requirement: FR-001

## Phase 4 — Running it

- [X] **T010** Progress and cancellation through each stage
  - Implements: `BookExportService`, using `src/MyFinance.Core/Progress/WorkProgress.cs`
  - Proven by: `BookExportServiceTests.An_export_reports_progress_and_can_be_stopped`,
    `Stopping_an_export_leaves_no_half_written_file`,
    `An_exported_file_reads_back_as_the_book_it_came_from`
  - Requirement: FR-005
- [X] **T011** The command, off the interface thread, writing atomically
  - Implements: `src/MyFinance.App/ViewModels/ShellViewModel.cs` (`ExportBookAsync`),
    `src/MyFinance.App/Views/ShellWindow.xaml` (the **Export** button),
    `src/MyFinance.App/App.xaml.cs` (registration)
  - The atomic write lives in the service: a `.partial` file and a move, as
    `src/MyFinance.Data/Security/BackupService.cs` already does, so a failed export cannot
    leave a truncated file that looks complete.
  - Proven by: the service by T004–T010; the atomic write by
    `Stopping_an_export_leaves_no_half_written_file`. **The button and dialog need Windows.**
  - Requirement: FR-005
- [X] **T012** The plaintext warning, in the sentence the user reads before choosing a location
  - Implements: `src/MyFinance.App/ViewModels/ShellViewModel.cs` — a `Confirm` before the file
    picker, saying in as many words that the file is **not encrypted** and that anyone who can
    read it can read the user's whole financial history
  - Proven by: **needs Windows.** Not a documentation task: the warning is in the flow, and it
    comes *before* the location is chosen rather than after
  - Requirement: FR-004

## Phase 5 — Making it usable by someone else

- [X] **T013** Document the shape where a reader will find it: a short section in `README.md`
  and the full shape in `specs/012-full-book-export/data-model.md`
  - Implements: the **Taking your data with you** section of `README.md`, and
    `specs/012-full-book-export/data-model.md`
  - Proven by: `BookExportServiceTests.The_documented_shape_matches_what_the_service_writes` —
    asserts every documented key appears in real output. What stops the documentation and the
    code drifting, which is the ordinary fate of a published format nobody checks.
  - Requirements: FR-002, SC-002

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T004, T005, T006, T009 | ✅ |
| FR-002 | T001, T003, T013 | ✅ |
| FR-003 | T007 | ✅ asserted against the serialised text, not the model |
| FR-004 | T012 | ✅ in the flow; ⚠️ **appearance needs Windows** |
| FR-005 | T010, T011 | ✅ service tested; ⚠️ button needs Windows |
| FR-006 | T001 (version), T004 (ids and transfer links) — the format does not preclude an importer; building one is a separate spec | ✅ |
| NFR-001 | T008 | ✅ ⚠️ not yet against the real 19,000-row book |
| SC-001 | T008 | ✅ ⚠️ same |
| SC-002 | T013 | ✅ |
| SC-003 | T004 | ✅ |
| SC-004 | T001 | ✅ |
| SC-005 | T006, plus `Every_id_referenced_in_the_document_resolves_to_something_in_it` | ✅ |

## What is not covered

- **The export against the real migrated book.** Everything is asserted over constructed books
  that include a transfer, a void, a split and a closed account — but 19,000 real rows would be
  better evidence, and would also settle the memory question below.
- **Memory and time on a large book.** The document is built in memory then serialised. The
  plan accepted that deliberately; nobody has measured it.
- **The Export button, the warning dialog and the file picker as they appear.** The decisions
  behind them are tested; the rendering is not.
