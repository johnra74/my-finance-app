# Implementation Plan: The encrypted book

**Spec:** `./spec.md` · **Status:** As-built

## Summary

Whole-database encryption with SQLCipher, keyed by Argon2id over the user's password and a
per-book random salt. The salt and cost parameters live in a small plaintext sidecar beside
the database, because they are needed *before* the database can be opened. The decision this
turns on: **encrypt everything rather than selected columns**, so that no future feature can
add a plaintext field by accident.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 6 — store nothing you do not need | Password never stored, in any form. The sidecar holds only non-secret parameters. |
| 7 — nothing leaves the machine | Key derivation and verification are entirely local; no recovery service exists to phone. |
| 3 — writes through a service | `Book.CreateContext()` is the only route to a context, and it is handed out by the security layer. |
| 1 — integer cents | The schema stores `Money` as `INTEGER`; round-tripping is asserted at this level. |

## Technical context

- **Runtime:** .NET 10, `net10.0`. Platform-neutral — the whole feature is testable on Linux.
- **Projects:** `MyFinance.Data/Security`, `MyFinance.Core/Security` (strength meter only).
- **Dependencies:**
  - `SQLitePCLRaw.bundle_e_sqlcipher` — SQLCipher, statically bundled. Chosen so the
    application ships one file with no native prerequisite on the machine.
  - `Konscious.Security.Cryptography.Argon2` — Argon2id. .NET has no in-box Argon2.
  - `Microsoft.EntityFrameworkCore.Sqlite` — schema and migrations.
- **Testing:** `tests/MyFinance.Data.Tests/Security` — real encrypted files on disk, not
  in-memory doubles. An encryption test against a fake is worth nothing.

## Design

### The two files

`name.mfdb` is the SQLCipher database. `name.mfmeta` is JSON: `{version, kdf, salt,
iterations, memoryKib, parallelism, keyBytes, createdUtc}`. Both are needed to open the
book, and that is the cost of not hard-coding the parameters. It buys the ability to raise
costs for new books over time without stranding old ones, and it makes the KDF auditable by
anyone who wants to look.

### Verifying the password

SQLCipher accepts *any* key at `PRAGMA key` time and only fails when it must decrypt a real
page. So `Open` runs a probe read (`EncryptedConnectionFactory.CanRead`) before returning a
`Book`. Without it, a wrong password would surface as a corruption error at some arbitrary
later moment, in whichever screen happened to read first.

### Ordering, so a failure is never worse than a no-op

- **Create:** database and schema first, sidecar last. A crash between them leaves a
  database with no sidecar, which reports as *missing* — and the `catch` deletes both anyway.
- **Change password:** the current password is proven by a full open; then `PRAGMA rekey`
  rewrites every page; the sidecar is advanced **only after** that returns. If the rekey
  throws, the on-disk file is still under the old key and the old parameters still describe
  it, so the old password still works.

### Alternatives rejected

- **Column-level encryption of "sensitive" fields.** Requires a permanent judgement about
  which fields are sensitive, and every future column is a chance to get it wrong. A memo is
  as revealing as an amount.
- **PBKDF2 or a plain hash.** Neither is memory-hard; both hand an offline attacker cheap
  GPU parallelism, which is the entire threat here.
- **Storing a password verifier** (a hash used to check the password before deriving).
  Would make the sidecar a target for offline attack in its own right, and it buys nothing:
  the probe read already verifies, and it verifies the thing that actually matters.
- **Keeping the salt inside the database.** Impossible — it is needed to open the database.
- **DPAPI or a Windows credential store.** Would tie a book to one machine and one Windows
  account, defeating "copy it to a memory stick and take it with you", and would put recovery
  in the hands of an operating-system component this project does not control.

## Project structure

```
src/MyFinance.Core/Security/PasswordStrength.cs      strength bands for the create screen
src/MyFinance.Data/Security/BookFileService.cs       create, open, re-key
src/MyFinance.Data/Security/BookKeyDerivation.cs     Argon2id
src/MyFinance.Data/Security/BookKeyParameters.cs     the sidecar, and its version gate
src/MyFinance.Data/Security/BookKey.cs               a key that zeroes itself
src/MyFinance.Data/Security/Book.cs                  the open handle, context factory
src/MyFinance.Data/Security/EncryptedConnectionFactory.cs   PRAGMA key, probe read
src/MyFinance.Data/Security/BookExceptions.cs        BookNotFound vs IncorrectPassword
```

## Risks

- **The sidecar is a single point of failure.** Losing it is exactly as fatal as forgetting
  the password, and it is a small file that looks unimportant. Contained by
  `010-backup-and-restore`, which archives the pair as one unit rather than trusting anyone
  to remember the second file.
- **Cost parameters age.** 4 iterations / 64 MiB / 2 lanes is a 2026 judgement. Because the
  parameters are per-book, raising them later affects new books only — but nothing today
  prompts an existing book to be re-keyed at a higher cost. Acceptable: the user can change
  their password to get new parameters.
- **`PRAGMA rekey` is proportional to file size** and holds the file for its duration. On a
  large book this is a long operation and is treated as one by the caller.
