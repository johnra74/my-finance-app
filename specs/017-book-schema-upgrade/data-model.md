# Data Model: Opening a book written by another version

This feature adds one fact — a version number — in two places, and two exception types. It
changes no existing entity.

## The version, twice

| Where | Field | Role |
|---|---|---|
| `<name>.mfmeta` (plaintext sidecar) | `schemaVersion` (int) | The **early** copy. Readable before the key is derived, so a newer book is refused without spending half a second on Argon2id. |
| The database, `AppSetting` | `schema.version` | The **authoritative** copy. Written by every upgrade, inside the upgrade's transaction. |

**On disagreement the database is believed and the sidecar is repaired.** The sidecar can
legitimately lag — a process could die between the two writes — but it cannot legitimately
lead.

### The sidecar, after this change

```json
{
  "version": 1,
  "schemaVersion": 4,
  "kdf": "argon2id",
  "salt": "<base64>",
  "iterations": 4,
  "memoryKib": 65536,
  "parallelism": 2,
  "keyBytes": 32,
  "createdUtc": "2026-09-05T00:00:00+00:00"
}
```

Note the two are different things and both are needed: `version` is the **sidecar format**
version, gating how to read this file (it already exists, and already refuses a newer value);
`schemaVersion` is the **database schema** version, gating whether this build may open the
book at all.

### Absent means current

A sidecar written before this feature has no `schemaVersion`, and a database written before it
has no `schema.version` row. Both are read as **the schema at the time this feature ships**,
not as unknown. Treating absence as unknown would make the feature's first act be to refuse
every book that exists.

## Relationship to EF Core's migrations history

`__EFMigrationsHistory` stays what it is: the record of which migrations have been applied,
and the thing `Database.Migrate()` acts on. It is **not** the version check, because it
answers "which migrations have run here" and not "was this file written by a build newer than
me". EF reads the columns it recognises and ignores those it does not, which is exactly the
silent case this feature exists to catch.

`SchemaVersion.Current` (new) is a single integer in code, incremented deliberately whenever a
migration is added. It is what makes the comparison possible at all.

## Exceptions

| Type | Raised when |
|---|---|
| `BookTooNewException` | The book's schema version exceeds `SchemaVersion.Current`. Carries both numbers so the message can name them. |
| `BookUpgradeException` | An upgrade could not be started (no verified backup) or did not complete. Carries the path of the pre-upgrade backup, because FR-006 requires the user to be told what to restore. |

Both derive from `BookFileException`, alongside the existing `BookNotFoundException` and
`IncorrectPasswordException` — the same distinction those two exist to make, extended.
