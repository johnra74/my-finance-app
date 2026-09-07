# Feature Specification: Exporting the whole book

**Folder:** `012-full-book-export`
**Created:** 2026-09-05
**Status:** **Implemented** (2026-09-05)
**Input:** Identified while retro-specifying the codebase, as the largest gap between the
constitution and the shipped product.

## Why this is the first gap

Constitution **principle 10** says: *Microsoft Money's file format is the reason this
application exists. Nothing produced here may be readable only from inside it.*

Today that principle is **half kept**. Any report exports to CSV
(`008-reports-and-dashboard`, FR-028). The **book itself does not export at all**. A
MyFinance book is an encrypted SQLCipher file whose key derivation is documented but whose
schema is not, and there is no path out of it except through this application.

That is materially the situation the user was in with Money — which is what
`009-money-migration` exists to rescue them from. Building the escape hatch inward and not
outward is the one inconsistency in this codebase that the codebase itself argues against.

## User Scenarios & Testing

### Primary user story

The user wants to know that if this application stops being maintained, is lost, or simply
stops suiting them, their twenty-five years of records come with them. They want to produce a
file that another program — or a person with a text editor — can read, covering everything the
book holds, not just what one report shows.

### Acceptance scenarios

1. **Given** an open book, **When** the user exports it, **Then** a file is produced
   containing every account, category, payee, transaction, split and transfer link.
2. **Given** that file, **When** it is opened in another program, **Then** the transactions
   are readable without this application.
3. **Given** an export, **When** it is compared against the book, **Then** every account's
   closing balance agrees.

### Edge cases

- An archived category or a closed account — both appear. An export that quietly omits
  history is not an export.
- A scheduled series with no occurrences yet entered, and a budget month with no figure —
  both appear, so the shape of the file does not depend on how much has been used.
- A book of ~20,000 transactions — the export must stay responsive and cancellable.
- Categories that were archived, and accounts that were closed, must appear.

## Requirements

### Functional requirements

- **FR-001**: The system MUST export the complete book: accounts, categories, payees,
  transactions, splits and transfer links, **and** scheduled bills, budgets, categorization
  rules, payee memory, merchant-code mappings and import batches. A partial export leaves part
  of the user's work trapped, which is what principle 10 forbids.
- **FR-002**: The exported form MUST be readable without this application: **a single JSON
  document** with a documented, versioned shape.
- **FR-003**: The system MUST NOT include full account numbers or the password (there are
  none stored — constitution 6 — and the export must not reintroduce them).
- **FR-004**: The export is **plaintext**, and the system MUST say so **at the point of
  export** — in the sentence the user reads before choosing where to write it, not in a
  manual. It is the least protected copy of the book that will ever exist.
- **FR-005**: The export MUST run off the interface thread with progress, and be cancellable
  (constitution 9).
- **FR-006**: The document MUST NOT preclude a future importer — stable ids, explicit
  transfer links, and a declared format version — but **building that importer is not part of
  this feature**. It is a separate spec, and committing to a round trip here would double the
  work and the test surface for a capability nobody has asked for yet.

### Non-functional requirements

- **NFR-001**: Every account's closing balance in the export MUST agree with the book to the
  cent.

## Key Entities

Everything in `002-accounts-and-register`, `003-categories-and-payees`,
`005-category-suggestion` (rules and payee memory), `006-scheduled-bills` and `007-budgets`.
This feature persists nothing of its own; it defines a **document shape**, which is set out in
`./data-model.md` and which becomes a published interface the moment it ships.

## Success Criteria

- **SC-001**: Exporting the real migrated book and totalling it externally reproduces every
  account balance to the cent.
- **SC-002**: A person with no access to this application can read a transaction, its date,
  amount, payee and category out of the exported file.
- **SC-003**: Every transfer in the book appears in the document as a pair whose two legs
  name each other, so a reader can reconstruct the link without inferring it from amounts and
  dates.
- **SC-004**: The document validates against its own declared schema, and carries a format
  version.
- **SC-005**: **Every id referenced anywhere in the document resolves to an entity present in
  it.** A published format may not carry a dangling reference.

## Assumptions

- Depends on constitution principles **10** (the point of the feature), **6** (nothing
  secret is added), **9** (progress and cancellation).
- **JSON was chosen over QIF, CSV-per-table and an unencrypted SQLite copy.** QIF is what
  this application already reads, so a round trip would be nearly free — but QIF cannot express
  transfers across a whole book, which is the same limitation `004-statement-import` FR-011
  documents from the other direction. CSV loses all structure. An SQLite copy publishes the
  internal schema as an interface and is the least protected artefact of the four. The
  reasoning is in `./plan.md`.

## Out of Scope

- Exporting to a competing product's native format.
- Continuous or scheduled export. `010-backup-and-restore` is the durability story; this is
  the *portability* story, and conflating them would produce a backup nobody should keep.

## Clarifications

### 2026-09-05

- **Q: What form should the export take?** → **One JSON document** with a documented,
  versioned shape (FR-002). Chosen over QIF, CSV-per-table and an unencrypted SQLite copy.
- **Q: How much of the book does it cover?** → **Everything**, including scheduled bills,
  budgets, rules and payee memory (FR-001). A partial export leaves part of the user's work
  trapped.
- **Q: Is the export encrypted, or plaintext with a warning?** → **Plaintext, with the warning
  at the point of export** (FR-004). Encrypting it would recreate the problem the feature
  exists to solve: a file readable only by this application. *(Assumed from the feature's own
  premise rather than asked.)*
- **Q: Must the export be re-importable — a round trip?** → **Not in this feature.** The
  format must not preclude one; building it is a separate spec (FR-006). *(Assumed.)*

