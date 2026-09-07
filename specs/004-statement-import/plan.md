# Implementation Plan: Importing a bank statement

**Spec:** `./spec.md` · **Status:** As-built

## Summary

Two layers with a hard line between them. `MyFinance.Import` parses, cleans, detects
duplicates and analyses signs as **pure functions over plain snapshots, with no database
dependency at all**. `MyFinance.Data/Services/ImportService` orchestrates: it matches the
account, resolves payees and categories, and writes, all inside one transaction. The
decision this turns on: **prepare and preview first, write second** — nothing reaches the
book until the user has seen what it would do.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 7 — nothing leaves the machine | Files only. No network code exists in this path. |
| 6 — store nothing you do not need | The account link is an HMAC digest under a per-book secret; the account number is never written. |
| 9 — off-thread, cancellable, transactional | `WorkProgress` through every stage; cancellation rolls back. |
| 5 — a guess is never applied unseen | The sign reversal is offered with both balances shown; duplicates without a reference are *suspected*, not dropped. |
| 2 — signs from the owning account | The whole point of the sign check. |

## Technical context

- **Projects:** `MyFinance.Import` (pure, `net10.0`, **no EF reference**),
  `MyFinance.Data/Services/ImportService.cs`, import dialogs in `MyFinance.App`.
- **Testing:** `MyFinance.Import.Tests` for parsing and the pure functions — milliseconds,
  no file, no encryption. `MyFinance.Data.Tests` for orchestration against real books.
  `tests/fixtures/ofx/redacted/` holds golden files put through the redactor.

## Design

### One parser for both OFX dialects

`OfxParser` produces an `OfxNode` tree from either 1.x SGML or 2.x XML. The SGML case is the
hard one: leaves have optional closing tags, so the parser treats a leaf's fields as
**siblings rather than nested children** and ignores a stray closing tag rather than
unwinding the document. Encoding comes from the header, with a byte order mark overriding a
header that contradicts it, and decoding happens once rather than repeatedly.

Being liberal is a requirement here, not a courtesy: every deviation tolerated has a real
bank's file behind it. A truncated file yields the rows it did contain.

### QIF: two passes, because the format records nothing

QIF states no version, no encoding and no locale. `QifConventions` scans the whole file
first to settle whether dates are day-first or month-first and whether a comma is a decimal
point or a thousands separator — a day above twelve proves day-first, a month above twelve in
second place proves month-first. A file that contradicts itself is flagged; a wholly
ambiguous one says so rather than picking. Reading row-by-row and guessing per row would
produce a ledger silently wrong by a factor of a thousand, or off by nine months.

### The account link is a digest

`OfxAccountKey` HMACs the bank id and account id under a per-book random secret held in
`AppSetting`. An HMAC and not a plain hash: account numbers carry far too little entropy to
hash safely, and a digest reversible by enumeration would defeat the point of not storing the
number.

### Duplicates: authority, then evidence

`DuplicateDetector` treats the bank's reference as authoritative — a match is certain and
cannot be overridden, an unseen reference is new even when amount and date agree. Only rows
with no reference fall back to amount, a nearby date and the payee, and those are reported as
*likely* or *possible* and imported by default rather than dropped. Three identical amounts
in a week are three transactions until something says otherwise.

### The sign check

`SignConventionAnalyzer` compares the balance the rows would produce against the balance the
statement states. That is evidence. Transaction types agreeing with the amounts is weaker
evidence, used only where the balance proves nothing — a new account, or too few typed rows,
where it abstains instead. A reversal is **never applied automatically**: both projections
are shown and the user chooses.

### Payee cleaning, and the key that makes a correction stick

`DescriptorCleaner` strips processor prefixes, branch numbers, trailing state codes and
reference numbers, and recases what is left. Separately it derives a **stable key** from the
parts of the descriptor that do not vary between visits, and a user's correction is stored
against that key (as a `PayeeAlias`), so the same shop lands on the same payee next month.
Cleaning is a display concern; the key is the identity.

### Writing, and undoing

Every import is an `ImportBatch`. Undo removes exactly its rows — and is refused once a row
has been reconciled, or a second time. It deliberately **keeps** the payees and descriptor
mappings the import taught: those were learned, the transactions were the mistake.

### Alternatives rejected

- **A strict parser.** Would refuse a meaningful share of real bank files. The fixture set
  exists to prove liberality is bounded rather than accidental.
- **Applying the sign reversal automatically when the balance disagrees.** Tempting, and it
  would usually be right. A statement imported backwards is the one failure that corrupts a
  book without announcing itself, so it stays a decision.
- **Storing the account number to match statements.** Rejected under constitution 6.
- **Dropping suspected duplicates silently.** A dropped genuine transaction is invisible; a
  flagged false positive costs one glance.
- **Importing QIF transfers as linked transactions.** Would invent a row in an account the
  user never imported, and duplicate it when they do.
- **Applying the OFX time zone.** Would move transactions across midnight and break matching
  against rows already imported.

## Project structure

```
src/MyFinance.Import/StatementFileReader.cs        format detection from contents
src/MyFinance.Import/Ofx/OfxParser.cs              SGML + XML into one node tree
src/MyFinance.Import/Ofx/OfxEncodingSupport.cs     charset, BOM precedence
src/MyFinance.Import/Ofx/OfxValueParser.cs         dates, amounts, time zones
src/MyFinance.Import/Ofx/OfxStatementReader.cs     nodes into statements
src/MyFinance.Import/Ofx/OfxStatementMapper.cs     account types, statuses
src/MyFinance.Import/Qif/QifConventions.cs         the whole-file locale scan
src/MyFinance.Import/Qif/QifParser.cs              rows, splits, categories
src/MyFinance.Import/Payees/DescriptorCleaner.cs   readable name + stable key
src/MyFinance.Import/Payees/DescriptorRules.cs     the strip rules
src/MyFinance.Import/Payees/PayeeMatcher.cs        descriptor to existing payee
src/MyFinance.Import/Dedupe/DuplicateDetector.cs   reference first, then evidence
src/MyFinance.Import/Model/SignConventionAnalyzer.cs
src/MyFinance.Import/Model/{ImportedStatement,ImportDiagnostic}.cs
src/MyFinance.Data/Services/{ImportService,ImportModels,OfxAccountKey}.cs
```

## Risks

- **A bank file shape nobody has seen.** Contained by salvaging rather than refusing, and by
  reporting every diagnostic rather than swallowing it.
- **Descriptor cleaning is heuristic** and always will be. Contained by keeping the raw text
  in the memo, always, and by making a correction permanent via the stable key.
- **The redaction path is a privacy risk in itself** — a fixture committed unredacted would
  be permanent. Contained by the gitignore rules (constitution 8) and by
  `FixtureTests.Redaction_removes_the_account_number_and_the_merchant`.
