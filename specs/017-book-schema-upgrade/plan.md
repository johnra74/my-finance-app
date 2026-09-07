# Implementation Plan: Opening a book written by another version

**Spec:** `./spec.md` · **Status:** **Built** — see `./tasks.md` for what shipped

## Summary

Stamp the schema version into the book and mirror it in the plaintext sidecar; check it in
`BookFileService.Open` before any query runs; **refuse** a book newer than this build
understands; and take a verified backup before applying any upgrade, refusing to proceed if
that backup cannot be written. The decision this turns on: **refuse what you do not
understand** — the pattern `BookKeyParameters.FromJson` already applies to the sidecar,
extended to the database it describes.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 6 — store nothing you do not need | A version integer, in a file that already carries non-secret parameters. |
| 9 — cancellable, all-or-nothing | The upgrade runs in a transaction and reports progress; interrupting it leaves the book as it was. |
| 3 — writes through a service | The upgrade path lives in `BookFileService`, beside `Create` and `ChangePassword`. |
| 10 — the user's data stays theirs | The failure this prevents is a book nobody can open or trust. |

## Technical context

- **Projects:** `MyFinance.Data/Security` only. No new dependency.
- **Existing pieces to reuse rather than rebuild:**
  - `BookKeyParameters` — already versioned (`version: 1`), already refuses a newer sidecar
    and an unknown KDF. The new field goes here and the refusal copies its shape.
  - `BookFileService.Open` — already does a probe read before handing back a `Book`. The
    version check belongs immediately after it and before anything else.
  - `BackupService` — already checkpoints the WAL, verifies by reading back, and moves
    atomically. The pre-upgrade backup is a call, not new code.
  - `EncryptedConnectionFactory` — the connection the version query runs on.
- **Testing:** `tests/MyFinance.Data.Tests/Security/BookFileServiceTests.cs`, which already
  covers the adjacent refusals and constructs real encrypted books on disk.

## Design

### Two copies of the version, and why

The **database** holds the authority: EF Core's own migrations history plus an explicit
`schema.version` in `AppSetting`, written by every upgrade.

The **sidecar** holds a mirror, as `schemaVersion` in the existing JSON. This is not
redundancy for its own sake — it is the only copy that can be read *before* the database is
opened, which is precisely the argument that put the salt and cost parameters there. It lets
the application say "this book was written by a newer version" without first deriving a key
and decrypting a page.

A disagreement between the two is itself a fault and is reported. The sidecar can legitimately
lag if a process died between the two writes, so the **database is believed** and the sidecar
is repaired.

### Refuse, do not adapt

A book whose version is greater than this build's is refused with a message naming the
situation and the remedy: use the newer build. It is not opened read-only. Read-only sounds
kinder and means every write path in every service must honour a flag, and the first path that
forgets writes a row shaped for a schema it does not understand — into the one file with no
recovery.

### All-or-nothing, by copying rather than by a transaction

**This is where the implementation departed from the plan, and improved on it.** The plan
assumed migrations could be applied "in a transaction". They cannot: EF Core wraps each
*individual* migration, not the *sequence* of them, so a five-migration upgrade that fails on
the fourth leaves a book three migrations in — a state nothing can read and nobody can
inspect, because the file is encrypted.

So the migration is applied to a **copy**, and the copy replaces the original only once every
migration has succeeded (`File.Move(overwrite: true)`, then the stale `-wal`/`-shm` are
deleted). The original is untouched until the last moment, which delivers FR-004 as written
rather than approximately.

The backup is still taken first, and is not redundant: copy-and-replace protects against a
migration that **fails**; it cannot protect against one that **succeeds and is wrong**.

### Back up before upgrading, or do not upgrade

`BackupService` already verifies by reading back what it wrote. That property is what makes it
usable here: the upgrade proceeds only if a **verified** backup exists, and refuses outright
if one cannot be written. A failed upgrade then has an answer to "what now" — and FR-006
requires the failure message to name that backup and where it is, rather than reporting an
exception.

### Order of operations on open

1. Read the sidecar. If `schemaVersion` is newer than this build → refuse, database untouched.
2. Derive the key and probe-read, as today.
3. Read the authoritative version from the database. Newer → refuse.
4. Older → announce the upgrade, take and verify a backup, apply migrations in a transaction,
   write both version copies, report.
5. Equal → open, as today.

The sidecar check comes first because it is the cheapest and because refusing there costs no
key derivation — half a second the user does not spend to be told no.

### Alternatives rejected

- **Read-only for a newer book.** See above. Large, and its failure mode is the one this
  feature exists to prevent.
- **The version in the database only.** Cannot be read before opening, so the newer-book case
  is discovered after key derivation and after EF has already looked at tables it may not
  understand.
- **The version in the sidecar only.** The sidecar is plaintext and separable; a book restored
  or copied without it, or with a stale one, would be misjudged.
- **Relying on EF Core's migrations history alone.** It answers "which migrations have run",
  not "was this written by a build newer than me" — EF reads what it recognises and ignores
  columns it does not know exist, which is the silent-corruption case.
- **Upgrading without a backup, inside a transaction only.** A transaction protects against a
  failed *statement*; it does not protect against a migration that succeeds and is wrong.

## Project structure

```
src/MyFinance.Data/Security/BookKeyParameters.cs   + schemaVersion, + its version gate
src/MyFinance.Data/Security/BookFileService.cs     the open-time check and the upgrade path
src/MyFinance.Data/Security/BookSchema.cs          NEW — Current, Unstamped, Compare
src/MyFinance.Data/Security/BookExceptions.cs      + BookTooNew, BookUpgradeRequired, BookUpgrade
src/MyFinance.Data/Services/SettingsService.cs     the schema.version key
src/MyFinance.App/ViewModels/StartupViewModel.cs   the prompt, and the failure message
tests/MyFinance.Data.Tests/Security/BookFileServiceTests.cs   extended
tests/MyFinance.Data.Tests/Security/SchemaUpgradeTests.cs     NEW
```

## Risks

- **Books written before this feature carry no version.** They must be treated as version 1 —
  the current schema — rather than as unknown, or the feature's first act is to refuse every
  existing book. This is the one place where "refuse what you do not understand" must not
  apply, and the plan makes it a named task rather than an afterthought.
- **Testing "an older build opens a newer book" needs two builds.** Simulated by writing a
  higher version into a real book and opening it with the current code — which tests the
  check, not the whole scenario. The limitation is stated in the task, and it still stands.
- **The pre-upgrade backup makes opening slower**, once, on a version change only. Acceptable:
  it is the moment when losing the book is possible.

## What changed during implementation

Three departures from the plan above, recorded because a plan that quietly disagrees with the
code it produced is worse than no plan.

1. **The type is `BookSchema`, not `SchemaVersion`.** `BookKeyParameters` needed a
   `SchemaVersion` property, and a class of the same name in the same namespace makes
   `SchemaVersion.Current` bind to the property inside that class. Renaming the type was
   cheaper than qualifying every reference.
2. **A third exception was needed.** The plan listed `BookTooNewException` and
   `BookUpgradeException`. `Open` also has to say *"this book is older and I am not going to
   upgrade it behind your back"*, which is neither of those — hence
   `BookUpgradeRequiredException`. Without it, `Open` would either upgrade silently (against
   FR-005) or report an older book as a generic failure.
3. **All-or-nothing is delivered by copying, not by a transaction** — see the section above.
   The plan's assumption was wrong, and the replacement is stronger.

