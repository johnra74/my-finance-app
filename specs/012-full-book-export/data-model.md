# Data Model: Exporting the whole book

This feature persists nothing. It defines a **document shape**, and that shape becomes a
published interface the day it ships — which is why it is versioned from the first release and
why it lives in `MyFinance.Core/Export` rather than being a reflection of the database schema.

## The document

```json
{
  "format": "myfinance-book",
  "formatVersion": 1,
  "exportedUtc": "2026-09-05T12:00:00+00:00",
  "application": { "name": "MyFinance", "version": "1.0.0" },
  "accounts":   [ … ],
  "categories": [ … ],
  "payees":     [ … ],
  "transactions": [ … ],
  "scheduled":  [ … ],
  "budgets":    { "lines": [ … ], "watchedCategoryIds": [ … ] },
  "rules":      [ … ],
  "merchantCodes": [ … ],
  "importBatches": [ … ]
}
```

`formatVersion` is present from version 1 so a reader can refuse what it does not understand
— the same rule the sidecar already applies in `001-encrypted-book` and that `017` extends to
the database.

## Two conventions that run through the whole document

### Amounts are strings, and also minor units

```json
{ "amount": "-123.45", "amountMinorUnits": -12345 }
```

**Sibling properties, not a nested object.** The first implementation wrapped them in an
`ExportAmount` object, which serialised as `"amount": { "amount": "-123.45", … }` — caught by
`No_amount_is_serialised_as_a_json_number` before it ever shipped. A document is read by
people, and that is not a thing anyone should have to look at.

`Money` is integer cents (constitution 1). Written as a bare JSON number, `-123.45` becomes a
binary double in most readers, and the exactness the entire application is built on is lost at
the last possible step. The string is what a person and a spreadsheet read; the minor units
are what a careful program reads. Neither alone is sufficient.

### Ids are the book's own, and references use them

Every entity carries its database id. Transfers name their far leg, splits name their
transaction, transactions name their account and payee. These ids are meaningless outside this
book, and that is fine: they exist so the graph can be **reconstructed rather than inferred**,
which is precisely what QIF cannot do and why it was rejected.

## Entities

### accounts

Id, name, type, group, institution, **masked** account number, opening balance, opened-on,
currency code, closed and favourite flags, sort order, last reconciled date and balance, notes.

**Never** a full account number and **never** the `OfxAccountKey` digest. The book does not
hold the first (constitution 6) and the second is a keyed value with no meaning outside this
book — exporting it would be exporting a secret to no purpose.

### categories

Id, leaf name, parent id, kind (income/expense), tax-related flag, **archived flag**, sort
order, and the rendered `fullName` for a reader who does not want to walk the tree.

Archived categories are included. An export that omits history is not an export.

### payees

Id, name, normalized name, last category id, last amount, active flag, notes, and the alias
list. Payee memory is decades of decisions and is one of the things worth most in this file.

### transactions

Id, account id, date, sequence-in-day, amount, payee id, memo, number, cleared status, void
flag, **`transferPeerId`** (the far leg's id), import batch id, and the splits — each with its
category id, amount and memo.

Splits are nested rather than a sibling array: a split has no meaning apart from its
transaction, and nesting makes it impossible to export one without the other.

### scheduled

Id, account, payee, memo, amount, estimate flag, payment method; the recurrence (frequency,
interval, start date, end condition, second day of month, weekend shift); the behaviour
(auto-enter, days ahead, active); the category template; and the occurrence history — which
due dates were entered or skipped.

The history matters: without it, a reconstructed book would not know which occurrences had
already been dealt with, which is exactly the fact `006-scheduled-bills` FR-018 exists to
record.

### budgets

Per line: category id, period start, period type, amount, rollover flag, notes. Plus the
watched-category list.

### merchantCodes

Industry code and the category it maps to. Editable book data like anything else — the
starting set comes from Money's own curated table (`009-money-migration`) — so leaving it out
would drop work the user may have done. Nearly missed for exactly the reason import batches
nearly were: it does not look like *their* data until you notice they can edit it.

### rules

Id, name, priority, enabled flag, account restriction, the conditions, and what the rule does
(category id, payee id). Exported in **priority order**, because for rules the order *is* part
of the meaning (`005-category-suggestion`, FR-009).

### importBatches

Id, when it ran, the source file name, the account, and the row count.

Present because transactions carry `importBatchId`, and **a document must not publish an id
that resolves to nothing.** It is also worth having on its own terms: it answers "where did
this row come from" — the March statement, or my own typing — which is provenance an archival
copy should keep and which nothing else in the document records.

Only the batch's own fields are exported, not its rows: the rows are the transactions, and
they are already there pointing back.

## Deliberately absent

| Not exported | Why |
|---|---|
| The password, or anything derived from it | The book does not hold it. Constitution 6. |
| Full account numbers | Never stored. Constitution 6. |
| `OfxAccountKey` | A keyed digest, meaningless outside this book, and a secret with no purpose in the export. |
| The per-book HMAC secret (`import.ofx.account_key_secret`) | Same. |
| Cached payee embeddings | Derived data, regenerable, and ~1 KB per payee of vectors nobody can read. |
| The trained classifier | Never persisted at all (`005`, FR-019). |
| Running balances, uncategorized counts, report figures | All derived. Exporting a derived value invites a reader to trust it over the rows it came from. |

**Note on what is *not* in that table:** import batches were nearly omitted as internal undo
machinery, which would have left `importBatchId` on every imported transaction pointing at
nothing. Either the entity is exported or the id is not — a published format may not carry a
dangling reference, and of the two, keeping the provenance is the more useful answer.
