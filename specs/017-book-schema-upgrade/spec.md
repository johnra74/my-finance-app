# Feature Specification: Opening a book written by another version

**Folder:** `017-book-schema-upgrade`
**Created:** 2026-09-05
**Status:** **Implemented** (2026-09-05)
**Input:** Identified while retro-specifying `001-encrypted-book`. This is the gap with the
worst consequence, even though it is the least visible.

## Why this is a gap

EF Core migrations exist and `BookFileService.Create` runs `context.Database.Migrate()` on a
new book. What is **not specified anywhere** is what happens when an *existing* book meets a
different build:

- An **older book** opened by a **newer** executable — does it upgrade in place? Silently?
  Does it take a backup first? What happens if the upgrade fails half-way?
- A **newer book** opened by an **older** executable — an entirely plausible case, since the
  application is a single file the user copies around and may have two copies of. EF Core will
  not warn about columns it does not know exist; it will read what it recognises and ignore
  the rest, and writes from the older build may then be inconsistent with the newer schema.

The consequences are unusually severe here because of what this application is:
**one encrypted file, often the only copy, with no password recovery and no server-side
backup** (constitution 6, and `001-encrypted-book`). A migration that half-applies to an
encrypted database is not something a user can inspect or repair.

By contrast, `BookKeyParameters` already gets this right and shows the pattern: a sidecar
written by a newer version is **refused explicitly** rather than misread
(`001-encrypted-book`, FR-015). The database has no equivalent guard.

## User Scenarios & Testing

### Primary user story

The user upgrades to a new build and opens their book. It either upgrades, having protected
itself first, or it tells them clearly what is wrong and what to do — but it never leaves them
with a file that opens and is subtly wrong.

### Acceptance scenarios

1. **Given** a book written by an older build, **When** a newer build opens it, **Then** it is
   upgraded, and a backup of the pre-upgrade state exists before the upgrade begins.
2. **Given** an upgrade that fails part-way, **When** the user tries again, **Then** the book
   is either fully upgraded or fully as it was — never in between.
3. **Given** a book written by a **newer** build, **When** an older build opens it, **Then**
   it is **refused with an explanation**, not opened read-only and not opened at all.
4. **Given** any book, **When** it is opened, **Then** the schema version it was written by is
   discoverable.

### Edge cases

- The upgrade fails because the disk is full, or the file is on a network share that
  disconnects.
- The user cancels mid-upgrade.
- Two copies of the executable of different versions on one machine, both pointed at the same
  book — plausible precisely because the application is a copyable single file.
- A book restored from a backup taken under an older schema (`010-backup-and-restore`).

## Requirements

### Functional requirements

- **FR-001**: A book MUST record the schema version it was written by, readable before the
  schema is used.
- **FR-002**: The system MUST refuse to open a book whose schema is **newer** than the
  executable understands, with a message naming the situation and the remedy.
- **FR-003**: The system MUST take a backup before applying any schema upgrade, and MUST NOT
  proceed if that backup cannot be written or verified.
- **FR-004**: A schema upgrade MUST be all-or-nothing.
- **FR-005**: The system MUST tell the user an upgrade is about to happen and what it has
  protected.
- **FR-006**: The system MUST report an upgrade failure in terms of what to do next — which
  backup to restore, and where it is.
- **FR-007**: The system MUST refuse a newer book **outright**, not open it read-only. A
  read-only mode would have to be honoured by every write path in the application, and a
  single missed path would corrupt exactly the file this feature exists to protect.

### Non-functional requirements

- **NFR-001**: The version check MUST happen before any query that assumes the current schema.
- **NFR-002**: An upgrade MUST report progress and MUST NOT appear hung (constitution 9).

## Key Entities

- **Schema version** — recorded in **both** the database and the plaintext sidecar. The
  sidecar copy is what allows the version to be checked *before* the database is opened at
  all, which is the same argument that put the key parameters there. The database copy is the
  authority; the sidecar copy is the one that can be read early, and a disagreement between
  them is itself a reportable fault.

## Success Criteria

- **SC-001**: A book written by a newer schema is refused by an older build, with a message
  naming the cause — and the book is unchanged afterwards.
- **SC-002**: An upgrade interrupted at any point leaves the book either fully upgraded or
  fully as it was.
- **SC-003**: Every upgrade is preceded by a verified backup, and the user can name where it
  is.
- **SC-004**: A book restored from a backup taken under an older schema upgrades on first open
  like any other.

## What the upgrades have carried

- **Schema 5 — a data repair, not a shape change.** The first upgrade whose migration changes
  no table: it decodes Microsoft Money's cheque-number sort key in books migrated before the
  reader knew about it (`009-money-migration` FR-008a). Recorded here because it widens what
  an upgrade means. Rewriting the user's records in place is a heavier act than adding a
  column — a wrong rule would corrupt values with no history to recover them from — and it is
  acceptable only because this feature already requires a **verified** backup first and
  refuses to proceed without one. That requirement was argued for shape changes; it earns its
  keep here.

## Assumptions

- Depends on constitution principles **6** (no recovery — which is what makes this severe),
  **9** (transactional, progress), and on `010-backup-and-restore` for the pre-upgrade
  protection.
- The precedent to follow is `BookKeyParameters`' version gate: **refuse what you do not
  understand** rather than reading it optimistically.

## Out of Scope

- Downgrading a book to an earlier schema.
- Repairing a corrupted database. Restore from a backup is the answer.

## Clarifications

### 2026-09-05

- **Q: Should a newer book be openable read-only by an older build, or refused outright?**
  → **Refused outright** (FR-007). Read-only is friendlier and would mean every write path
  honouring a mode flag; one missed path corrupts the file. Refusal follows the precedent
  `BookKeyParameters.FromJson` already sets for the sidecar.
- **Q: Is the schema version held in the database, the sidecar, or both?**
  → **Both**, with the database as the authority and the sidecar as the copy that can be read
  before opening. *(Assumed from the existing sidecar precedent rather than asked.)*

