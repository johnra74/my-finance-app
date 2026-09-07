# Implementation Plan: Backup and restore

**Spec:** `./spec.md` · **Status:** As-built

## Summary

`BackupService` writes a `.mfbak` zip holding both halves of the book plus a manifest;
`BookBackupService` owns the preferences, the on-close trigger and pruning. The decision this
turns on: **a backup is checkpointed, verified and atomic** — the write-ahead log is folded in
first, the archive is read back before it is accepted, and it is moved into place from a
`.partial` file.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 6 — store nothing you do not need | The archive holds exactly what the book holds; the sidecar is a salt, not a secret. |
| 7 — nothing leaves the machine | Backups are local files. Nothing is uploaded. |
| 9 — cancellable, nothing half-written | Progress reported; a `.partial` file plus an atomic move. |
| 1 / 3 | The database is copied, not rewritten, so no invariant can be violated in transit. |

## Technical context

- **Projects:** `MyFinance.Data/Security/BackupService.cs` (the archive),
  `MyFinance.Data/Services/BookBackupService.cs` (preferences, on-close, pruning).
- **Format:** a zip with **fixed internal entry names** — `book.mfdb`, `book.mfmeta`,
  `backup.json`.
- **Testing:** `BackupServiceTests` and `BookBackupServiceTests`, over real files.

## Design

### Both halves, or nothing

`name.mfdb` without `name.mfmeta` cannot be opened by anyone, ever. A backup that copies only
the database is not a backup, and expecting a user to remember a small unremarkable sidecar is
not a plan. So the archive holds the pair, and a book missing its sidecar is **refused**
rather than backed up half-way.

### Checkpoint first

SQLite in WAL mode keeps recent work in a side file. Copying the database without folding that
in produces an archive missing everything since the last checkpoint — which is exactly the
session the user is trying to protect. `Changes_made_moments_before_the_backup_are_in_it` is
the test that guards it.

### Verify, then move

The archive is written to a `.partial` file, read back to confirm it is a valid archive
containing what it should, and only then moved into place. A backup nobody has read is a
guess, and the second it costs catches a full disk or a failing drive at the only moment
anything can still be done about it. The atomic move is what makes a failure leave the
previous backup intact.

### Fixed internal names

`book.mfdb`, `book.mfmeta`, `backup.json` — never the original file names. Extraction
therefore cannot be steered by a crafted entry path (zip slip), and restore needs no
guesswork about which entry is which.

### Pruning is narrow on purpose

Only automatic backups, only in the automatic folder, only for **this** book, only down to a
clamped count, keeping the newest. A folder the user chose is never pruned: a copy someone
deliberately put on a memory stick is theirs, and an application that deletes it has done
something unforgivable for the sake of tidiness.

### Restore belongs to the opening screen

Restoring replaces the files a session is using. Doing that under a running session leaves it
writing into a database that no longer exists. So restore is offered only before a book is
open — and it clears any journal files left at the target path, which would otherwise be
paired with the wrong database.

### Alternatives rejected

- **Copying the `.mfdb` alone.** The most natural mistake, and fatal.
- **Skipping the checkpoint.** Faster, and silently loses the current session.
- **Trusting the write.** A backup nobody has read is a guess.
- **Writing directly to the final path.** A failure would destroy the previous backup on the
  way to not producing a new one.
- **Preserving original entry names in the zip.** Invites zip-slip and complicates restore
  for no benefit.
- **Pruning by age.** Count is what the user can reason about.
- **Restore from inside an open book.** See above.

## Project structure

```
src/MyFinance.Data/Security/BackupService.cs      archive, checkpoint, verify, atomic move
src/MyFinance.Data/Services/BookBackupService.cs  preferences, back up now, on close, prune
src/MyFinance.App/  the Backups menu, and restore on the opening screen
```

Preferences: `backup.automatic`, `backup.keep`, `backup.directory`.

## Risks

- **A backup that has never been restored is a hypothesis.** Contained by
  `A_restored_book_opens_with_the_same_password_and_holds_the_same_data` — a full round trip,
  not a file-exists check.
- **Pruning is the destructive operation here.** Contained by four separate tests for what it
  must *not* touch.
- **A user may still lose the password**, and no backup helps. Stated plainly in the
  interface; see `001-encrypted-book`.
