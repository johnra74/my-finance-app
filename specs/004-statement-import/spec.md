# Feature Specification: Importing a bank statement

**Folder:** `004-statement-import`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "Import the `.ofx`/`.qfx`/`.qbo`/`.qif` files my banks let me download, without
duplicating what I already have and without getting the signs backwards."

## User Scenarios & Testing

### Primary user story

Once a month the user downloads a file from each bank's website and wants their register to
match the statement. The import must add exactly what is new, recognise which account the
file belongs to, turn bank descriptors back into merchant names, and be undoable in one
action if it goes wrong. It runs on files that no two banks produce identically, several of
which are subtly malformed.

### Acceptance scenarios

1. **Given** a downloaded file, **When** it is opened, **Then** the format is determined from
   its contents, not its extension.
2. **Given** a statement previously imported for an account, **When** a later statement from
   the same bank arrives, **Then** the account is recognised without the user choosing it.
3. **Given** a statement overlapping one already imported, **When** it is imported, **Then**
   only the rows not already present are added.
4. **Given** a credit-card file whose amounts run the opposite way, **When** it is previewed,
   **Then** the balance the import would produce is shown beside the balance the bank states,
   and a switch is offered to reverse the file.
5. **Given** a descriptor `SQ *BLUE BOTTLE 1234 NEW YORK NY`, **When** it is imported,
   **Then** the payee reads `Blue Bottle New York`, the raw text is kept in the memo, and a
   correction the user makes is honoured by the next import.
6. **Given** an import the user regrets, **When** they undo it from the import history,
   **Then** exactly its rows are removed and the balance is restored.
7. **Given** a large file, **When** it is imported, **Then** the application keeps repainting,
   says which stage it is on and how far through, and can be stopped — leaving the account
   exactly as it was.
8. **Given** a bank that rejected the request, **When** the file is opened, **Then** the
   bank's own message is shown, not "0 transactions found".

### Edge cases

- A truncated file → the rows it did contain are imported, and the truncation reported.
- A file with no closing tags on its leaves (OFX 1.x SGML) → parses.
- A stray closing tag → ignored rather than unwinding the document.
- Windows-1252 bytes with a header declaring that charset → decoded correctly; a UTF-8 BOM
  overrides a header claiming ASCII.
- An investment statement → refused, or its investment sections skipped and reported, rather
  than partly read.
- A QIF file with no locale information → the whole file is scanned before anything is read,
  to settle whether `03/02/2026` is March or February and whether `1,234` is one thousand.
  A file that contradicts itself is flagged; a wholly ambiguous one says so.
- A row with an unreadable date or amount → skipped and reported; the rest import.
- Three identical amounts in one week → not collapsed into one.
- Selecting no rows → refused, rather than writing an empty batch.
- An account this version does not model → import refused.

## Requirements

### Functional requirements

**Reading the file**

- **FR-001**: The system MUST determine the format from the file's contents, not its
  extension.
- **FR-002**: The system MUST read both OFX dialects with one parser: 1.x SGML with optional
  closing tags, and 2.x XML.
- **FR-003**: The system MUST honour the declared character encoding, and MUST let a byte
  order mark override a contradicting header.
- **FR-004**: The system MUST salvage a malformed or truncated file as far as it goes, rather
  than refusing it whole.
- **FR-005**: The system MUST surface a bank's own error message when the file carries one.
- **FR-006**: The system MUST refuse a file that is not a statement at all, distinguishably
  from one that is empty.
- **FR-007**: The system MUST return every statement in a file that carries several.
- **FR-007a**: The system MUST refuse an investment statement rather than partly reading it,
  and where investment sections sit alongside readable bank sections it MUST skip them and say
  so. Partly reading one would produce a register missing the trades that moved the cash in
  it. *(Investment accounts are `013-investment-accounts`.)*
- **FR-008**: The system MUST parse an OFX timestamp's time zone but MUST NOT apply it.
  Converting `20260101190000[-5:EST]` to UTC would move the transaction to 2 January,
  desynchronising the register from the paper statement and breaking duplicate matching
  against rows imported earlier.
- **FR-009**: For QIF, the system MUST scan the whole file before reading any row, to settle
  date order and decimal separator, and MUST report a file that is ambiguous or
  self-contradictory rather than guessing.
- **FR-010**: The system MUST import QIF splits and the file's own categories, offering to
  create categories the book lacks and reporting those it will not invent.
- **FR-011**: The system MUST import a QIF transfer (`L[Savings]`) as an ordinary
  transaction, not a linked one. A QIF export covers a single account, so the far leg is not
  in the file; inventing one would create a transaction in an account the user never
  imported, which would then be duplicated when that account's own export arrives.

**Matching the account**

- **FR-012**: The system MUST recognise which account a statement belongs to, after the first
  import from that source.
- **FR-013**: The system MUST store a keyed digest of the bank and account identifiers and
  MUST NOT store the account number itself. The digest MUST be an HMAC under a per-book
  secret, not a plain hash — account numbers carry too little entropy to hash safely.
- **FR-014**: The system MUST refuse to import into an account type it does not model.

**Duplicates**

- **FR-015**: The system MUST treat the bank's own reference as authoritative: a matching
  reference is certain, and an unseen reference is new even when the amount and date match.
- **FR-016**: The system MUST match rows carrying no reference on amount, a nearby date and
  the payee, and MUST present those as *suspected* rather than silently dropping them.
- **FR-017**: The system MUST keep a reference repeated inside one file only once.

**Signs**

- **FR-018**: The system MUST compare the balance the import would produce against the
  balance the statement states, and MUST offer to reverse the file when they disagree.
- **FR-019**: The system MUST NOT invert a statement on a heuristic alone. A statement
  imported backwards is the one failure that corrupts a book without announcing itself, and a
  heuristic that is usually right is not good enough to apply silently.
- **FR-020**: The system MUST abstain when there is too little evidence — on a new account,
  or with too few typed rows.

**Payees**

- **FR-021**: The system MUST clean bank descriptors into readable merchant names, stripping
  processor prefixes, branch numbers, trailing state codes and reference numbers, while
  keeping the parts that distinguish real payees.
- **FR-022**: The system MUST keep the raw descriptor in the memo, always.
- **FR-023**: The system MUST remember a user's correction against the stable part of the
  descriptor, so the same shop lands on the same payee next month.
- **FR-024**: The system MUST create one payee for a merchant appearing many times in one
  file.

**Writing**

- **FR-025**: The system MUST record every import as a batch, and MUST allow it to be undone
  in one action.
- **FR-026**: The system MUST refuse to undo an import once one of its rows has been
  reconciled, and MUST refuse to undo the same import twice.
- **FR-027**: Undoing MUST keep the payees and the descriptor mappings the import taught.
  Those were learned; the transactions were the mistake.
- **FR-028**: The system MUST mark imported rows `Cleared` — they have reached the bank by
  definition, which is exactly what that status means.
- **FR-029**: The system MUST give rows sharing a date distinct, increasing sequences.
- **FR-030**: The system MUST let the user exclude rows in the preview, and MUST NOT write
  what was excluded.
- **FR-031**: The system MUST refuse an empty selection rather than writing an empty batch.

### Non-functional requirements

- **NFR-001**: Parsing, cleaning, duplicate detection and sign analysis MUST have no database
  dependency, so they are pure functions over plain snapshots, testable in milliseconds.
- **NFR-002**: An import MUST run off the interface thread, report its stage and progress,
  and be cancellable; progress MUST never go backwards and MUST finish complete.
- **NFR-003**: Cancelling MUST leave the account exactly as it was — the writes run inside one
  database transaction.
- **NFR-004**: The system MUST NOT contact any bank. File import only; no credentials are
  ever collected or stored.

## Key Entities

- **Imported statement** — what a file yielded: account identifiers, currency, a date range,
  a stated ending balance, the rows, and everything that went wrong while reading it.
- **Imported transaction** — date, amount, the bank's reference, the raw descriptor, the
  type the bank stated, an optional merchant code, an optional cleared flag.
- **Import batch** — one import, recorded so it can be undone: when, which file, which
  account, and the rows it wrote.

## Success Criteria

- **SC-001**: Every redacted fixture statement parses and yields usable transactions, and
  reconciles against its own stated balance. *`FixtureTests.Every_redacted_statement_parses`,
  `Every_redacted_statement_has_usable_transactions`,
  `Redacted_statements_still_reconcile_against_their_stated_balance`.*
- **SC-002**: Re-importing the same file adds nothing; an overlapping statement adds only its
  new rows. *`ImportServiceTests.Re_importing_the_same_file_adds_nothing`,
  `An_overlapping_statement_imports_only_its_new_rows`.*
- **SC-003**: A statement whose amounts are reversed is caught by comparing against the
  stated balance, and one that balances as written is left alone.
  *`SignConventionAnalyzerTests.A_card_statement_whose_amounts_are_reversed_is_caught_by_the_balance`,
  `A_correctly_signed_statement_is_left_alone`,
  `A_bank_statement_is_never_inverted_on_a_heuristic_alone`.*
- **SC-004**: The account is recognised by the next statement from the same bank, and what is
  stored is a digest and never the account number.
  *`ImportServiceTests.An_account_is_recognised_by_the_next_statement_from_the_same_bank`,
  `Linking_stores_a_digest_and_never_the_account_number`.*
- **SC-005**: Two visits to the same merchant produce the same descriptor key; genuinely
  different merchants get different keys; volatile trailing parts do not change it.
  *`DescriptorCleanerTests.Two_visits_to_the_same_merchant_produce_the_same_key`,
  `Genuinely_different_merchants_get_different_keys`,
  `Volatile_trailing_parts_do_not_change_the_key`.*
- **SC-006**: A user's correction is honoured by the next import.
  *`ImportServiceTests.A_correction_is_remembered_and_honoured_by_the_next_import`.*
- **SC-007**: Undoing an import removes exactly its rows and restores the balance, keeps what
  it learned, and is refused after reconciliation or a second time.
  *`ImportServiceTests.Undoing_an_import_removes_exactly_its_rows_and_restores_the_balance`,
  `Undoing_keeps_the_payees_and_the_mappings_it_learned`,
  `Undoing_is_refused_once_a_row_has_been_reconciled`, `Undoing_the_same_import_twice_is_refused`.*
- **SC-008**: The resulting register balance matches what the statement says.
  *`ImportServiceTests.The_resulting_balance_matches_what_the_statement_says`.*
- **SC-009**: Progress is honest — it never goes backwards and finishes complete — and
  stopping leaves nothing half-written.
  *`ProgressAndCancellationTests.Counted_progress_never_goes_backwards_and_finishes_complete`,
  `Progress_describes_itself_honestly`.*
- **SC-010**: A file the parser cannot use reports failure rather than throwing.
  *`Anything_unreadable_reports_failure_rather_than_throwing`.*

## Assumptions

- Depends on constitution principles **7** (nothing leaves the machine — file import only),
  **6** (a digest, never the account number), **9** (off-thread, cancellable, transactional),
  **2** (signs from the owning account's view), **5** (a guess is never applied unseen — the
  sign switch is offered, not taken).
- Banks are inconsistent, and being liberal in what is accepted is a requirement rather than
  a courtesy. Every deviation the parser tolerates has a fixture behind it.
- The user downloads files themselves. OFX Direct Connect was considered and rejected.

## Out of Scope

- Direct Connect, OAuth bank links, or any aggregator. Permanently — see constitution 7.
- CSV statement import. See the gap table in `specs/README.md`.
- Investment transaction import — see `013-investment-accounts`.
- The category a row should get. That is `005-category-suggestion`.
