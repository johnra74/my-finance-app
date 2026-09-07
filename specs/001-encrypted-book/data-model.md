# Data Model: The encrypted book

This feature owns two things: the on-disk shape of a book, and the sidecar. It does not own
the schema inside the database — that belongs to the features that write into it.

## Files

| File | Encrypted | Required to open | Contents |
|---|---|---|---|
| `<name>.mfdb` | Yes, wholly | Yes | The SQLCipher database. Every table, index and value. |
| `<name>.mfmeta` | No | Yes | JSON key-derivation parameters. Nothing secret. |

Both must be backed up together. See `specs/010-backup-and-restore`.

## Sidecar format (version 1)

```json
{
  "version": 1,
  "kdf": "argon2id",
  "salt": "<base64, 16 random bytes>",
  "iterations": 4,
  "memoryKib": 65536,
  "parallelism": 2,
  "keyBytes": 32,
  "createdUtc": "2026-09-05T00:00:00+00:00"
}
```

- **version** — refused if greater than this build understands. A silently misread key
  parameter set would produce a wrong key and present as a wrong password, which would send
  the user looking for the wrong problem.
- **kdf** — refused if not `argon2id`. Present so a future migration to another function is
  a data change and not a guess.
- **salt** — unique per book; regenerated on every password change, so the new key is
  unrelated to the old.
- **keyBytes** — 32. SQLCipher's raw key size.

## In-memory objects

- **`BookKey`** — 32 bytes, zeroed on dispose, and unreadable afterwards.
- **`BookKeyParameters`** — an immutable record; the sidecar's serialized form.
- **`Book`** — path + key + parameters. Hands out `MyFinanceDbContext` instances until
  disposed, then refuses.

## Book-scoped secrets stored *inside* the database

One secret lives in the encrypted database rather than the sidecar, and it is worth naming
here because it is easy to mistake for key material:

- **`import.ofx.account_key_secret`** (`AppSetting`) — a random per-book secret used to HMAC
  bank and account identifiers so a downloaded statement can be recognised without storing
  the account number. It protects nothing about the book's own encryption, and it is safe
  inside the encrypted database precisely because it is only ever needed once the book is
  already open. See `specs/004-statement-import`.
