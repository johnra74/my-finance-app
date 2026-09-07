# Feature Specification: Bringing a Microsoft Money book across

**Folder:** `009-money-migration`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "Read my `.mny` file — twenty-five years of records — and bring the whole thing
into this application, without needing Money installed and without risking the file."

## User Scenarios & Testing

### Primary user story

The user has a Microsoft Money file they have kept since the 1990s. Money is discontinued.
They want everything in that file — accounts, categories, payees, every transaction with its
splits, transfers and reconciliation state — in this application, and they want to be able to
check it worked by reading the result against Money's own screen, which is still on the
machine. The original file must not be touched, because it may be the only copy.

### Acceptance scenarios

1. **Given** a `.mny` file, **When** it is opened, **Then** it is read directly, with no
   Microsoft component, no ODBC driver and no copy of Money required.
2. **Given** the file, **When** the wizard runs, **Then** it shows what is in it before
   writing anything.
3. **Given** an empty book, **When** the migration runs, **Then** accounts, categories,
   payees and every transaction are written, with splits, transfers and reconciliation state
   intact.
4. **Given** Money's three-level category tree, **When** it is migrated, **Then** it becomes
   this application's two levels plus an income-or-expense flag, with nothing invented and
   nothing dropped — because Money's top level is only ever the two roots INCOME and EXPENSE.
5. **Given** decades of payee decisions, **When** the migration completes, **Then** each
   payee remembers the category it was last filed under, so the next imported statement is
   categorized from that history.
6. **Given** the migration is running, **When** the user stops it, **Then** the book is left
   completely untouched.
7. **Given** the migration has finished, **When** the last step of the wizard is shown,
   **Then** every account is listed with its closing balance, laid out to be read straight
   against Money's own account list.

### Edge cases

- A book that already has entries → refused. Merging twenty thousand transactions into a book
  that already has some would make duplicates nobody could untangle afterwards.
- A file with no accounts → refused rather than half-written.
- Money's own encrypted catalog → stepped around by identifying tables from their columns.
- A file with a **password** set on it → encrypts the data as well; reported, with the
  instruction to remove the password in Money and save a copy.
- A date outside the calendar → refused rather than throwing. Money writes sentinel dates.
- Payees that normalize alike → folded together.
- Closed accounts → brought across by default, and can be left behind on request.
- Projected-but-never-entered bills → kept by default, and can be left out.
- Investment and loan accounts → arrive as balance-only accounts with their cash
  transactions.

## Requirements

### Functional requirements

**Reading the file**

- **FR-001**: The system MUST read the Jet 4 / MSISAM file format directly, with no Microsoft
  component, no ODBC driver and no copy of Money.
- **FR-002**: The system MUST only ever **read** the file. It MUST NOT open it for writing,
  move it, rename it or change it.
- **FR-003**: The system MUST identify the tables it needs **by their columns**, because
  Money encrypts the system catalog in every file.
- **FR-004**: The system MUST refuse to identify a table when two candidates match, rather
  than picking one.
- **FR-005**: The system MUST decode Money's text encodings, including the compressed form
  that switches between one and two bytes per character mid-run.
- **FR-006**: The system MUST read dates as days from 30 December 1899, treat a zero date as
  no date, and refuse a date beyond the calendar rather than throwing.
- **FR-007**: The system MUST read currency values as ten-thousandths and convert them exactly
  into integer cents.
- **FR-008**: The system MUST yield exactly the row count each table declares.
- **FR-008a**: The system MUST decode Money's **cheque-number sort key** rather than copying it
  verbatim. `TRN.szId` is not what the user typed: Money prefixes it with a one-character type
  flag and, for a number, right-aligns the digits in a twelve-character field so that sorting
  the column as plain text orders cheques numerically and puts references after them. Copied
  verbatim it reaches the register as `0        1168` and `1ATM`.

  | Flag | Shape | Meaning |
  |---|---|---|
  | `0` | exactly 13 characters: the flag plus a right-aligned number | a numeric cheque number |
  | `1` | the flag plus the text as typed, unpadded | a non-numeric reference |

  Both shapes were confirmed by scanning a reference file, where every flag-`0` value was
  exactly 13 characters long without exception.

  The decoding MUST be **narrow**: a number entered by hand can also begin with `1`, so the
  flag is believed only where the remainder could not be the value itself — the full padded
  shape, or a remainder with no digit in it. Everything else passes through untouched.
- **FR-009**: The system MUST produce the same result reading the same file twice.
- **FR-010**: The system MUST report clearly when a file is password-protected and therefore
  unreadable, distinguishing that from a corrupt file.

**Writing the book**

- **FR-011**: The system MUST write the whole book in one transaction, and MUST refuse to
  migrate into a book that already has entries.
- **FR-012**: The system MUST replace the seeded starter chart of categories with the user's
  own.
- **FR-013**: The system MUST flatten Money's three-level tree into two levels plus an
  income-or-expense flag, with every parent present and no cycles.
- **FR-014**: The system MUST bring across payees, fold payees that normalize alike, and keep
  transactions pointing at the survivor.
- **FR-015**: The system MUST write **payee memory** — each payee's last category — because
  that is what makes a migrated book immediately useful rather than merely complete.
- **FR-016**: The system MUST bring across Money's merchant-code table, which is the only
  source in the categorization chain able to categorize a shop the user has genuinely never
  dealt with.
- **FR-017**: The system MUST preserve splits, and every migrated transaction's splits MUST
  add up to it; an uncategorized transaction MUST still get one split.
- **FR-018**: The system MUST link both legs of every transfer to each other, and the two legs
  MUST cancel.
- **FR-019**: The system MUST give transactions sharing a day a stable order.
- **FR-020**: The system MUST bring closed accounts across by default, with an option to leave
  them behind.
- **FR-021**: The system MUST keep projected-but-never-entered bills by default, with an
  option to leave them out, and MUST say which choice is in effect — because Money counts
  those rows in its own balances, and leaving them out gives a tidier book whose figures no
  longer match Money's.
- **FR-022**: The system MUST bring an account type it does not model across as a balance-only
  account with its cash transactions.
- **FR-023**: The system MUST NOT import account numbers. Money stores them encrypted, and
  this application does not keep full numbers anyway.
- **FR-024**: The system MUST NOT convert Money's recurring bill definitions. They MUST be
  counted and reported instead: the frequency codes cannot be verified against a single file,
  and a wrong due date is worse than no due date. *(See `014-scheduled-bill-migration`.)*

**Running it, and checking it**

- **FR-025**: The system MUST report each stage as it runs — categories, payees, accounts,
  transactions, transfers.
- **FR-026**: The system MUST be stoppable at any point, rolling the whole thing back.
- **FR-027**: The system MUST end with a list of every account and its closing balance, laid
  out to be read against Money's own account list. A migration that cannot be verified is one
  that should not be trusted.

### Non-functional requirements

- **NFR-001**: Reading a 19 MB file with ~19,000 transactions MUST complete in a time the user
  will wait for, with progress shown throughout.
- **NFR-002**: The reader MUST be platform-neutral, so it can be tested without Windows.
- **NFR-003**: The reader MUST bound the file size it will attempt, rather than trusting a
  header.
- **NFR-004**: No test committed to this repository may assert a balance, an account name or a
  payee from a real Money file. Tests assert **structural invariants** only.

## Key Entities

- **Money file** — accounts, categories, payees, transactions with splits and transfer links,
  and the merchant-code table, as read out of the `.mny`.
- **Migration options** — keep closed accounts, keep projected bills.
- **Migration report** — what was written, stage by stage, plus the closing balance per
  account for verification.

## Success Criteria

- **SC-001**: Every table **the migration reads** yields exactly the row count it declares.
  *`MoneyReaderTests.Every_table_the_migration_reads_yields_exactly_the_row_count_it_declares`;
  and `No_table_reads_short_except_the_one_known_overflow_row` for the rest of the file. See
  the known defect below.*
- **SC-002**: Reading the same file twice gives the same answer.
- **SC-002a**: No transaction number read from a real file still carries the sort flag, and a
  number that could have been typed by hand is never altered.
  *`MoneyNumberTests.The_sort_flag_is_read_off_and_nothing_else_is_touched`,
  `MoneyNumberTests.A_hand_typed_number_beginning_with_one_is_left_alone`,
  `MoneyReaderTests.No_transaction_number_still_carries_the_sort_flag`.*
- **SC-002b**: A book migrated before the decoding existed is repaired in place by the schema-5
  upgrade, and repairing it twice changes nothing.
  *`MoneyNumberRepairTests.The_repair_decodes_the_numbers_the_migration_left_behind`,
  `MoneyNumberRepairTests.The_repair_leaves_a_hand_typed_number_alone`,
  `MoneyNumberRepairTests.The_repair_and_the_reader_agree`,
  `MoneyNumberRepairTests.Running_the_repair_twice_changes_nothing_the_second_time`.*
  *`Reading_the_same_file_twice_gives_the_same_answer`.*
- **SC-003**: Every transaction belongs to an account that exists; the category tree has no
  cycles and every parent exists; every transfer leg names the account at the other end; the
  parts of a split add up to their parent.
  *`Every_transaction_belongs_to_an_account_that_exists`,
  `The_category_tree_has_no_cycles_and_every_parent_exists`,
  `Every_transfer_leg_names_the_account_at_the_other_end`,
  `The_parts_of_a_split_add_up_to_their_parent`.*
- **SC-004**: A real Money file migrates and every balance survives the journey, matching what
  Money reported. *`MoneyFileMigrationTests.A_real_money_file_migrates_and_every_balance_survives_the_journey`,
  `The_migrated_balance_matches_the_balance_money_reported`.* **These tests skip when no
  `.mny` is present**, which is most of the time — the file is gitignored (constitution 8), so
  CI proves the structural invariants and only a developer with the file proves this one.*
- **SC-005**: Both legs of every migrated transfer point at each other and cancel.
  *`Both_legs_of_every_migrated_transfer_point_at_each_other`,
  `The_two_legs_of_every_transfer_cancel_each_other`.*
- **SC-006**: Every migrated transaction has splits that add up to it.
  *`Every_migrated_transaction_has_splits_that_add_up_to_it`.*
- **SC-007**: Money's three-level categories become two levels plus a kind, with both sides of
  the tree represented. *`Money_three_level_categories_become_two_levels_plus_a_kind`,
  `Both_sides_of_the_category_tree_are_represented`.*
- **SC-008**: Stopping leaves the book completely untouched.
  *`ProgressAndCancellationTests.Stopping_a_migration_leaves_the_book_completely_untouched`.*
- **SC-009**: Migrating into a non-empty book, or from a file with no accounts, is refused
  rather than half-done. *`Migrating_into_a_book_that_already_has_entries_is_refused`,
  `A_file_with_no_accounts_is_refused_rather_than_half_written`.*
- **SC-010**: Every name decodes as readable text and every date lands inside the years a
  person could have kept books. *`Names_decode_as_readable_text`,
  `Dates_land_inside_the_years_a_person_could_have_kept_books`.*

### A caveat recorded rather than smoothed over

The reference screenshots — kept out of source control under `docs/design/money-screens/`,
because they are of a real book — are dated **August 2026**, while the `.mny` available for
development is older, and accounts visible in the screenshots do not exist in it. **The
screenshot-derived acceptance figures were therefore never validated against that file, and
this spec does not claim they were.** Correctness was established by other means: two
independent implementations agreeing to the cent, exact declared row counts on all 12 tables,
splits summing, every transfer pair cancelling, and closed accounts netting to exactly 0.00.

### A known reader defect

**One row per file can be lost to an overflow page.** `JetTable.Rows()` skips slots flagged
`0x4000` — rows whose data lives on another page — along with deleted rows, rather than
following them. On the reference file this loses exactly one row, from Money's advice and
alerts table, which nothing here reads.

The impact today is nil, and the defect is real: on a table the migration *does* read it would
be silent data loss, which is precisely what SC-001 exists to catch. It is recorded rather
than fixed because following an overflow pointer is Jet work in its own right. SC-001 has been
split accordingly — the strict, no-exceptions form now covers every table the migration reads,
and a second check still reports any *new* short read elsewhere.

## Assumptions

- Depends on constitution principles **11** (foreign files are read-only), **8** (no real
  financial data in source control), **6** (no account numbers), **9** (progress and
  cancellation, transactional), **1** (exact conversion into integer cents).
- Money signs an amount from the account's own point of view and writes a transfer as two
  rows that cancel — **the same conventions used here** — so the mapping is a translation
  rather than a rebuild.
- Money's category top level is only ever INCOME and EXPENSE. This is what makes the
  three-to-two flattening lossless.
- Money encrypts pages 1–14 (its system catalog) in every file, and the data pages beyond
  them only when a password is set.

## Out of Scope

- Investment holdings — securities, lots, prices, cost basis. See `013-investment-accounts`.
- Account numbers. See FR-023.
- Recurring bill definitions. See `014-scheduled-bill-migration`.
- Password-protected `.mny` files. The user is told to remove the password in Money and save
  a copy.
- Writing back to a `.mny` file, ever.
