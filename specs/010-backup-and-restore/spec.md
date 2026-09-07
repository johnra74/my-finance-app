# Feature Specification: Backup and restore

**Folder:** `010-backup-and-restore`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "There is no password recovery and no copy of my book on anybody's server. If I
lose it, it is gone. Backups cannot be an afterthought."

## User Scenarios & Testing

### Primary user story

A book is two files, and losing either one is fatal. The user needs backups that happen
without being remembered, that hold **both** halves, that are verified rather than assumed,
and that can be restored at the only moment restoring is safe. They also need to be able to
put a copy somewhere of their own choosing — a memory stick, a cloud folder — and know it will
never be tidied away by the application.

### Acceptance scenarios

1. **Given** a new book, **When** it is created, **Then** automatic backups are already
   switched on, keeping the newest ten in a folder beside the book.
2. **Given** an open book, **When** it is closed, **Then** a backup is taken if that is
   switched on — including everything done in that session.
3. **Given** an archive, **When** it is inspected, **Then** it holds **both** the database and
   the key sidecar, plus a description of the book it came from.
4. **Given** a backup being written, **When** it completes, **Then** it has been read back
   before being accepted.
5. **Given** ten automatic backups and a limit of ten, **When** an eleventh is taken, **Then**
   the oldest is pruned and the newest ten kept.
6. **Given** a backup the user saved to a folder of their own, **When** pruning runs, **Then**
   it is never touched.
7. **Given** a backup, **When** it is restored from the opening screen, **Then** the book
   opens with the password it had when the backup was taken, and holds the same data.
8. **Given** a restored book, **When** a wrong password is tried, **Then** it is still refused.

### Edge cases

- A failed backup → the previous one survives; no half-written file is left.
- A book whose sidecar is missing → refused rather than half backed up. A backup of one half
  is not a backup.
- A damaged file in the backup folder → does not hide the good ones.
- A file that is not a backup at all → reported clearly.
- A nonsense retention setting → clamped rather than obeyed; a limit below one prunes nothing.
- Restoring over an existing book → refused unless explicitly asked for.
- Journal files left by a previous book at the restore path → cleared, or the restored
  database would be read alongside another book's write-ahead log.
- Restoring while a book is open → not offered. Replacing the file underneath a running
  session would leave it writing into a database that no longer exists.

## Requirements

### Functional requirements

- **FR-001**: A backup MUST contain **both** halves of the book — the encrypted database and
  the key sidecar — in one archive.
- **FR-002**: The system MUST refuse to back up a book whose sidecar is missing.
- **FR-003**: The archive MUST carry a description of the book it came from.
- **FR-004**: The system MUST fold the write-ahead log into the database before copying it.
  Without that step the copy is missing everything done since the last checkpoint — precisely
  the session being protected.
- **FR-005**: The system MUST read every archive back before accepting it.
- **FR-006**: The system MUST write atomically, so a failure leaves no half-written archive
  and the previous backup survives.
- **FR-007**: The system MUST back up automatically on close, switched on by default for a new
  book.
- **FR-008**: The system MUST support backing up on demand, and backing up to a location the
  user chooses.
- **FR-009**: The system MUST keep automatic backups in a folder beside the book by default,
  with both the folder and the number kept configurable.
- **FR-010**: The system MUST prune automatic backups to the number asked for, keeping the
  newest, and MUST clamp a nonsensical retention setting rather than obeying it.
- **FR-011**: The system MUST NOT prune a folder the user chose. A copy they put somewhere is
  theirs.
- **FR-012**: The system MUST NOT prune backups belonging to a different book.
- **FR-013**: The system MUST name archives so they sort chronologically, and list them newest
  first.
- **FR-014**: The system MUST tolerate a damaged or foreign file in a backup folder without
  hiding the good ones, and MUST report a non-backup clearly.
- **FR-015**: The system MUST offer restore **only from the opening screen**, not from inside
  an open book.
- **FR-016**: The system MUST refuse to restore over an existing book unless explicitly asked
  to.
- **FR-017**: The system MUST clear journal files left by a previous book at the restore path.
- **FR-018**: A restored book MUST open with the password it had when the backup was taken,
  and MUST still refuse a wrong one.
- **FR-019**: The system MUST remember the backup preferences.

### Non-functional requirements

- **NFR-001**: The archive MUST be no less safe than the book itself — the database inside is
  encrypted under the same password and the sidecar holds only a salt, so it is exactly as
  safe in cloud storage as the original.
- **NFR-002**: Extracting an archive MUST NOT be able to write outside the target directory.
- **NFR-003**: Backing up MUST report progress and be cancellable, and cancelling MUST leave
  no half-written file.

## Key Entities

- **Backup archive (`.mfbak`)** — a zip holding the encrypted database, the key sidecar, and a
  manifest describing the book and when the backup was taken.
- **Backup preferences** — automatic on close, how many to keep, which folder.

## Success Criteria

- **SC-001**: An archive holds both halves and describes its origin.
  *`BackupServiceTests.A_backup_holds_both_halves_of_the_book`,
  `A_backup_describes_the_book_it_came_from`.*
- **SC-002**: Changes made moments before the backup are in it.
  *`Changes_made_moments_before_the_backup_are_in_it`* — the write-ahead log check, and the
  single most important assertion in this feature.
- **SC-003**: A restored book opens with the same password and holds the same data, and still
  refuses a wrong password. *`A_restored_book_opens_with_the_same_password_and_holds_the_same_data`,
  `The_restored_book_still_refuses_the_wrong_password`.*
- **SC-004**: A failed backup leaves the earlier one intact.
  *`An_earlier_backup_survives_a_failed_one`.*
- **SC-005**: A book with no key file is refused rather than half backed up.
  *`A_book_with_no_key_file_is_refused_rather_than_half_backed_up`.*
- **SC-006**: Pruning keeps the newest, never touches another book's backups, never touches a
  chosen folder, and does nothing below a limit of one.
  *`Pruning_keeps_the_newest_and_removes_the_rest`,
  `Pruning_never_touches_backups_of_a_different_book`,
  `A_backup_saved_somewhere_chosen_is_never_pruned`, `Pruning_to_fewer_than_one_does_nothing`,
  `Automatic_backups_are_pruned_to_the_number_asked_for`,
  `A_nonsense_retention_setting_is_clamped_rather_than_obeyed`.*
- **SC-007**: A damaged file does not hide the good ones, and a non-backup is reported
  clearly. *`A_damaged_file_in_the_folder_does_not_hide_the_good_ones`,
  `A_file_that_is_not_a_backup_is_reported_clearly`.*
- **SC-008**: Restoring clears a previous book's journal files and is refused over an existing
  book unless asked. *`Restoring_clears_journal_files_left_by_a_previous_book`,
  `Restoring_over_an_existing_book_is_refused_unless_asked_for`.*
- **SC-009**: Stopping a backup leaves no half-written file.
  *`ProgressAndCancellationTests.Stopping_a_backup_leaves_no_half_written_file`.*

## Assumptions

- Depends on constitution principles **6** (the archive contains no secret beyond what the
  book already holds), **9** (progress, cancellation, nothing half-written), **7** (backups
  are local files; nothing is uploaded anywhere).
- A book is two files and both are required. This is the fact the whole feature exists to
  handle — see `001-encrypted-book`.
- There is **no password recovery**. A backup cannot help somebody who has forgotten their
  password, and the application says so.

## Out of Scope

- Uploading backups anywhere. The user may put an archive in a synced folder themselves; the
  application does not talk to a service.
- Incremental or differential backups. A personal book is small; a whole copy is simpler and
  cannot be partially valid.
- Scheduled backups at a time of day. On close is the trigger.
- Restoring a single account or a date range from a backup.
