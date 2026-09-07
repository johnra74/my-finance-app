# Implementation Plan: Exporting the whole book

**Spec:** `./spec.md` · **Status:** **Built** — see `./tasks.md`

## Summary

A `BookExportService` walks the open book into one versioned JSON document covering
everything — ledger, bills, budgets, rules and payee memory — written off the interface thread
through the existing progress and cancellation machinery. The decision this turns on:
**JSON with explicit ids and explicit transfer links**, because the one thing every simpler
format loses is exactly the structure that makes a personal ledger a ledger.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 10 — the user's data stays theirs | This *is* principle 10. The spec set records it as the one principle currently only half kept, and this closes it. |
| 6 — store nothing you do not need | The export cannot reintroduce what the book never held: there is no password and no full account number to write. |
| 9 — off-thread, cancellable | `RunBusyAsync` + `WorkProgress`, as the import and migration paths already do. |
| 7 — nothing leaves the machine | The export writes a file the user chose. It does not upload, and there is no "share" affordance. |
| 1 — integer cents | Amounts are written as exact decimal strings from `Money.ToDecimal()`, never as JSON floats. |

## Technical context

- **Projects:** `MyFinance.Data/Services` (the service), `MyFinance.App` (the command).
- **Dependencies:** none new. `System.Text.Json` is in the framework, and the source-generated
  serializer keeps it single-file-friendly.
- **Existing pieces to reuse:**
  - `BusyViewModel.RunBusyAsync` and `MyFinance.Core/Progress/WorkProgress.cs` — the
    off-thread, progress-reporting, cancellable wrapper, already used by import and migration.
  - The existing services as the read path. Nothing new queries the database directly.
- **Deliberately not reused:** `ReportsPageViewModel.BuildCsv`. That writes a *rendered
  report* — the rows currently on screen, formatted for a spreadsheet. This writes the *book*.
  Conflating them would make one method serve two jobs that will diverge on the first change
  to either.

## Design

### Why JSON, and what the alternatives actually cost

- **QIF** — the format this application already parses, so an importer would be nearly free.
  But QIF describes **one account at a time** and cannot express a transfer across a book;
  `004-statement-import` FR-011 documents that limitation from the other direction, and it is
  the reason a QIF export would silently drop the links between paired rows.
- **CSV per table** — opens anywhere, and reassembling splits and transfers becomes the
  reader's problem. The export would be readable and not usable.
- **An unencrypted SQLite copy** — immediately queryable and nearly free to produce, and it
  makes the **internal schema a published interface**, so every future migration becomes a
  compatibility question. It is also the least protected of the four.
- **JSON** — expresses the structure, is readable by a person and by every language, and its
  shape is ours to version deliberately rather than inherited from the database.

### One document, not a folder

A single file cannot be half-copied, half-emailed or half-restored. The whole point is a
self-contained artefact the user can put somewhere and rely on.

### Amounts as strings, never as JSON numbers

`Money` is integer cents (constitution 1). Written as a JSON number, `123.45` becomes a
double in most readers and the exactness the whole application is built around is lost at the
last step. Amounts are written as decimal **strings** (`"123.45"`), with the minor units also
present, so a careless reader and a careful one both get a right answer.

### Ids are stable and internal

Every entity carries its database id, and references use it. This is what lets transfers name
their far leg, splits name their transaction, and a future importer reconstruct the graph. It
does leak internal ids into the document — acceptable, because they are meaningless outside
the book and the alternative is inventing a second identity scheme.

### The plaintext warning belongs in the sentence, not the manual

FR-004 is not a documentation task. The user chooses a location in a dialog, and that dialog
says the file is not encrypted and anyone who can read it can read everything. A book that
took Argon2id half a second to open should not become a plaintext file without the user being
told in the same breath.

### Verified by arithmetic, not by shape

The acceptance test is not "the file parses". It is: total the exported transactions per
account, add the opening balance, and every account's closing figure must match the book to
the cent — over the real migrated book, where the awkward cases (splits, transfers,
voids, closed accounts) actually exist.

### Alternatives rejected

- **QIF, CSV, SQLite** — above.
- **Encrypting the export.** Recreates the problem the feature exists to solve.
- **Exporting the ledger only.** Bills, budgets and rules are work the user did; leaving them
  in is leaving them trapped.
- **Streaming the document with a writer.** Simpler to build a model and serialise it; a book
  of ~20,000 transactions is tens of megabytes at worst, and the clarity is worth more than
  the memory. Revisit only if measurement says so.
- **Reusing `BuildCsv`.** Above.

## Project structure

```
src/MyFinance.Core/Export/BookDocument.cs          NEW — the document shape (records)
src/MyFinance.Data/Services/BookExportService.cs   NEW — walk the book, report progress
src/MyFinance.App/ViewModels/...                   the command, RunBusyAsync, the warning
tests/MyFinance.Core.Tests/Export/BookDocumentTests.cs        NEW
tests/MyFinance.Data.Tests/Services/BookExportServiceTests.cs NEW
```

The document shape lives in `MyFinance.Core` rather than in `Data`, so it can be tested
without a book and so a future importer can consume it without depending on the storage layer.

## Risks

- **The document shape becomes an interface the day it ships.** Contained by a declared format
  version in the file from the first release, and by the shape being ours rather than a
  reflection of the database schema.
- **A plaintext copy of everything is a real exposure**, and the feature is right anyway. The
  containment is the warning at the point of export and the fact that the user chose to make
  it.
- **An export that silently omits something** is the failure mode — worse than one that fails,
  because it looks complete. Contained by the balance-agreement test and by a count check per
  entity type against the book.
- **Memory on a large book.** ~20,000 transactions with splits, built in memory then
  serialised. Expected to be fine; measured rather than assumed in T009.

## What changed during implementation

Three corrections, all caught by tests rather than by review.

1. **Amounts are sibling properties, not a nested object.** The first cut wrapped each amount
   in an `ExportAmount` record, which serialised as
   `"amount": { "amount": "-123.45", "amountMinorUnits": -12345 }`.
   `No_amount_is_serialised_as_a_json_number` failed on it — the outer key's value was `{`
   rather than a quoted string — and the flat form is both what `data-model.md` had documented
   and what a person would want to read.
2. **Merchant-code mappings were missing.** They are editable book data (the starting set
   comes from Money's own table), and leaving them out would have dropped work the user may
   have done. Missed for the same reason import batches nearly were: it does not look like
   *their* data until you notice they can edit it.
3. **The golden test needed a richer book.** Nulls are omitted rather than written, so a thin
   fixture could not prove keys like `accountNumberMasked` and `transferPeerId` exist at all.
   The test now builds an account with a masked number and a real transfer.

