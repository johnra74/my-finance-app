# Tasks: Backup and restore

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

This feature owns no entities, so there is no `data-model.md`. Its data model is the archive
format, documented in `plan.md`.

## Phase 1 — The archive

- [X] **T001** Write a `.mfbak` holding both halves and a manifest, under fixed internal names
  - Implements: `src/MyFinance.Data/Security/BackupService.cs`
  - Proven by: `BackupServiceTests.A_backup_holds_both_halves_of_the_book`,
    `A_backup_describes_the_book_it_came_from`
  - Requirements: FR-001, FR-003, NFR-002
- [X] **T002** Refuse a book with no sidecar
  - Proven by: `A_book_with_no_key_file_is_refused_rather_than_half_backed_up`
  - Requirement: FR-002
- [X] **T003** Fold the write-ahead log in before copying
  - Proven by: `Changes_made_moments_before_the_backup_are_in_it`
  - Requirement: FR-004
- [X] **T004** Verify the archive by reading it back, then move it into place atomically
  - Proven by: `An_earlier_backup_survives_a_failed_one`
  - Requirements: FR-005, FR-006
- [X] **T005** Report a file that is not a backup, and do not let a damaged one hide the rest
  - Proven by: `A_file_that_is_not_a_backup_is_reported_clearly`,
    `A_damaged_file_in_the_folder_does_not_hide_the_good_ones`
  - Requirement: FR-014
- [X] **T006** Chronologically sortable names, listed newest first
  - Proven by: `The_suggested_name_sorts_chronologically`, `Backups_are_listed_newest_first`
  - Requirement: FR-013

## Phase 2 — Automatic backups

- [X] **T007** On by default for a new book; taken on close when switched on, not when off
  - Implements: `src/MyFinance.Data/Services/BookBackupService.cs`
  - Proven by: `BookBackupServiceTests.A_new_book_backs_itself_up_by_default`,
    `Closing_the_book_takes_a_backup_when_that_is_switched_on`,
    `Closing_the_book_takes_no_backup_when_that_is_switched_off`
  - Requirement: FR-007
- [X] **T008** Back up now, and back up to a chosen folder; the default folder beside the book
  - Proven by: `Backing_up_writes_into_a_folder_beside_the_book`, `The_default_folder_sits_beside_the_book`,
    `A_chosen_backup_folder_is_used_instead_of_the_default`
  - Requirements: FR-008, FR-009
- [X] **T009** Preferences remembered
  - Implements: `backup.automatic`, `backup.keep`, `backup.directory` via `SettingsService`
  - Proven by: `Preferences_are_remembered`
  - Requirement: FR-019
- [X] **T010** Pruning: newest kept, count clamped, other books untouched, chosen folders
  untouched, below one does nothing
  - Proven by: `Automatic_backups_are_pruned_to_the_number_asked_for`,
    `Pruning_keeps_the_newest_and_removes_the_rest`,
    `A_nonsense_retention_setting_is_clamped_rather_than_obeyed`,
    `Pruning_never_touches_backups_of_a_different_book`,
    `A_backup_saved_somewhere_chosen_is_never_pruned`, `Pruning_to_fewer_than_one_does_nothing`
  - Requirements: FR-010, FR-011, FR-012

## Phase 3 — Restore

- [X] **T011** Restore a book that opens with its original password and holds the same data
  - Implements: `BackupService`
  - Proven by: `A_restored_book_opens_with_the_same_password_and_holds_the_same_data`,
    `The_restored_book_still_refuses_the_wrong_password`
  - Requirement: FR-018
- [X] **T012** Refuse to overwrite unless asked; clear a previous book's journal files
  - Proven by: `Restoring_over_an_existing_book_is_refused_unless_asked_for`,
    `Restoring_clears_journal_files_left_by_a_previous_book`
  - Requirements: FR-016, FR-017
- [X] **T013** Restore offered only from the opening screen
  - Implements: the opening screen in `MyFinance.App`
  - Proven by: **by construction** — no restore command exists on any in-book screen.
    **Needs Windows** to confirm visually.
  - Requirement: FR-015

## Phase 4 — Staying usable

- [X] **T014** Progress and cancellation, leaving no half-written file
  - Proven by: `ProgressAndCancellationTests.Stopping_a_backup_leaves_no_half_written_file`
  - Requirement: NFR-003

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001, FR-003 | T001 | ✅ |
| FR-002 | T002 | ✅ |
| FR-004 | T003 | ✅ |
| FR-005, FR-006 | T004 | ✅ |
| FR-007 | T007 | ✅ |
| FR-008, FR-009 | T008 | ✅ |
| FR-010 – FR-012 | T010 | ✅ |
| FR-013 | T006 | ✅ |
| FR-014 | T005 | ✅ |
| FR-015 | T013 | ⚠️ by construction; visual check needs Windows |
| FR-016, FR-017 | T012 | ✅ |
| FR-018 | T011 | ✅ |
| FR-019 | T009 | ✅ |
| NFR-001 | By construction: the archive holds the book's own two files | ✅ |
| NFR-002 | T001 (fixed internal entry names) | ✅ |
| NFR-003 | T014 | ✅ |
