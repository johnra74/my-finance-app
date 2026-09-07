# Tasks: Opening a book written by another version

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** **complete**

`[X]` done · `[ ]` outstanding · `[P]` could run in parallel.
Every task names the file that implements it and the test that proves it. New coverage lives
in `tests/MyFinance.Data.Tests/Security/SchemaUpgradeTests.cs` — **24 tests, all passing**;
the suite went from 819 to 843.

Three things differed from the plan and are recorded at the end of `plan.md`: the type is
`BookSchema` rather than `SchemaVersion`, a third exception was needed, and all-or-nothing is
delivered by copy-and-replace rather than by a transaction — EF Core cannot wrap a *sequence*
of migrations, only each one.

## Phase 1 — Know what version this build is

- [X] **T001** Add `BookSchema` with `Current`, `Unstamped` and `Compare`, and the rule stated in its own doc comment: **increment
  it whenever a migration is added**
  - Creates: `src/MyFinance.Data/Security/BookSchema.cs`
  - Proven by: `SchemaUpgradeTests.The_current_schema_version_matches_the_migration_count` —
    asserts `Current` against the number of applied migrations, so adding a migration without
    bumping the version fails the build rather than shipping quietly
  - Requirement: FR-001
- [X] **T002** [P] Two new exception types carrying the numbers and the backup path
  - Changes: `src/MyFinance.Data/Security/BookExceptions.cs`
  - Adds: `BookTooNewException` (both version numbers), `BookUpgradeRequiredException` (an
    older book, announced rather than upgraded silently — **not in the original plan**), and
    `BookUpgradeException` (the pre-upgrade backup path)
  - Proven by: `SchemaUpgradeTests.A_failed_upgrade_names_the_backup_to_restore`, and
    through T005, T006 and T009
  - Requirements: FR-002, FR-006

## Phase 2 — Record the version, in both places

- [X] **T003** Add `schemaVersion` to the sidecar, reading an absent value as the current
  schema
  - Changes: `src/MyFinance.Data/Security/BookKeyParameters.cs`
  - Proven by: `SchemaUpgradeTests.A_sidecar_without_a_schema_version_reads_as_the_current_one`,
    `A_sidecar_round_trips_its_schema_version`
  - Requirement: FR-001
- [X] **T004** Write `schema.version` into `AppSetting` on create and on upgrade; read an
  absent row as the current schema
  - Changes: `src/MyFinance.Data/Security/BookFileService.cs` (`Create`),
    `src/MyFinance.Data/Services/SettingsService.cs` (the key)
  - Proven by: `SchemaUpgradeTests.A_new_book_records_its_schema_version_in_both_places`,
    `A_book_with_no_recorded_version_is_treated_as_current`
  - Requirement: FR-001

**T003 and T004 together carry the risk named in the plan:** a book written before this
feature must open normally. `A_book_with_no_recorded_version_is_treated_as_current` covers it,
and additionally proves the sidecar is *repaired* on that first open rather than left
unstamped for ever.

## Phase 3 — Refuse a book from the future

- [X] **T005** Check the sidecar's version **before deriving the key**; refuse a newer book
  - Changes: `src/MyFinance.Data/Security/BookFileService.cs` (`Open`)
  - Proven by: `SchemaUpgradeTests.A_book_from_a_newer_build_is_refused_before_the_key_is_derived`
    — asserts the refusal *and* that it happens without the Argon2id cost
  - Requirements: FR-002, NFR-001
- [X] **T006** Check the authoritative version in the database after the probe read; refuse a
  newer book, leaving it unchanged
  - Changes: `BookFileService.Open`
  - Proven by: `SchemaUpgradeTests.A_newer_schema_in_the_database_is_refused`,
    `A_refused_book_is_byte_for_byte_unchanged_afterwards`
  - Requirements: FR-002, NFR-001
- [X] **T007** Reconcile a disagreement between the two copies — believe the database, repair
  the sidecar, report it
  - Changes: `BookFileService.Open`
  - Proven by: `SchemaUpgradeTests.A_stale_sidecar_version_is_repaired_from_the_database`,
    `A_sidecar_claiming_a_newer_version_than_the_database_is_reported`
  - Requirement: FR-001

**Limitation, stated rather than hidden:** these tests write a higher version into a real book
and open it with the current code. That exercises the *check*, not two genuinely different
builds. Proving the whole scenario needs two published executables and a person. This has not
changed.

`A_book_from_a_newer_build_is_refused_before_the_key_is_derived` is worth reading: it passes
**the wrong password**, and still expects `BookTooNewException`. That is the proof no key was
derived — the sidecar check runs before Argon2id, so the password never gets as far as
mattering.

## Phase 4 — Upgrade an older book, safely

- [X] **T008** Announce the upgrade before it starts, saying what will be protected
  - Implements: `src/MyFinance.App/ViewModels/StartupViewModel.cs` — `TryUpgradeAsync`, and
    the `BookUpgradeRequiredException` / `BookTooNewException` arms of `UnlockAsync`
  - Proven by: **the prompt's appearance needs Windows.** The decision to prompt rather than
    upgrade silently is asserted by
    `SchemaUpgradeTests.An_older_book_is_reported_as_needing_an_upgrade_rather_than_upgraded_silently`
  - Requirement: FR-005
- [X] **T009** Take a **verified** backup before applying migrations, and refuse to proceed if
  one cannot be written
  - Implements: `BookFileService.Upgrade`, calling
    `src/MyFinance.Data/Security/BackupService.cs`
  - Proven by: `SchemaUpgradeTests.An_upgrade_takes_a_verified_backup_first` — which asserts
    the archive is *readable and describes this book* via `BackupService.Inspect`, not merely
    that a file appeared
  - Requirement: FR-003
- [X] **T010** Apply migrations all-or-nothing — **to a copy that replaces the original only
  once every migration has succeeded**, not inside a transaction as the plan assumed
  - Implements: `BookFileService.Upgrade`
  - Proven by: `SchemaUpgradeTests.An_upgrade_interrupted_part_way_leaves_the_book_as_it_was`
    (byte-for-byte identical afterwards, and no `.upgrading` file left),
    `A_completed_upgrade_advances_both_version_copies`,
    `An_upgrade_keeps_every_row_the_book_already_held`,
    `An_upgrade_with_the_wrong_password_changes_nothing`,
    `Upgrading_a_book_that_is_already_current_does_nothing`,
    `Upgrading_a_book_from_a_newer_build_is_refused`
  - EF Core wraps each individual migration but not the sequence of them, so upgrading in
    place could leave a book part-way through a multi-migration run — unreadable, and
    uninspectable because the file is encrypted.
  - Requirement: FR-004
- [X] **T011** Report an upgrade failure in terms of what to do next, naming the backup and
  where it is
  - Implements: `BookFileService` (`BookUpgradeException`), and the message in
    `src/MyFinance.App/ViewModels/StartupViewModel.cs`
  - Proven by: `SchemaUpgradeTests.A_failed_upgrade_names_the_backup_to_restore`.
    **The wording as displayed needs Windows.**
  - Requirement: FR-006
- [X] **T012** Progress reporting during the upgrade
  - Implements: `BookFileService.Upgrade`, using `src/MyFinance.Core/Progress/WorkProgress.cs`
  - Proven by: `SchemaUpgradeTests.An_upgrade_reports_progress_while_it_runs`, plus
    cancellation via `An_upgrade_interrupted_part_way_leaves_the_book_as_it_was`
  - Requirement: NFR-002

## Phase 5 — The case that ties it to the rest

- [X] **T013** A book restored from a backup taken under an older schema upgrades on first
  open like any other
  - Implements: nothing new — this asserts the two features compose, and they do.
    `BackupService.Restore` already deletes stale `-wal`/`-shm` files; `Upgrade` now does the
    same after replacing the database.
  - Proven by: `SchemaUpgradeTests.A_book_restored_from_an_older_backup_upgrades_on_first_open`
  - Requirement: SC-004

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T001, T003, T004, T007 | ✅ |
| FR-002 | T002, T005, T006 | ✅ |
| FR-003 | T009 | ✅ |
| FR-004 | T010 | ✅ delivered by copy-and-replace, which is stronger than planned |
| FR-005 | T008 | ✅ logic tested; ⚠️ **the prompt's appearance needs Windows** |
| FR-006 | T002, T011 | ✅ logic tested; ⚠️ the displayed wording needs Windows |
| FR-007 | T005, T006 — refusal is the whole implementation; there is no read-only mode to build | ✅ |
| NFR-001 | T005, T006 | ✅ |
| NFR-002 | T012 | ✅ |
| SC-004 | T013 | ✅ |

## What is not covered

Two things, both genuinely outside what this repository can run:

- **The upgrade prompt and the failure message as they appear on screen.** The decisions
  behind them are tested; the rendering is not.
- **A genuinely older executable meeting a newer book.** Simulated by stamping a version, not
  by running two builds. Closing this needs two published `.exe`s and a person.
