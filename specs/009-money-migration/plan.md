# Implementation Plan: Bringing a Microsoft Money book across

**Spec:** `./spec.md` · **Status:** As-built

## Summary

A pure-C# reader for the Jet 4 / MSISAM binary format, in `MyFinance.Import/Mny`, plus a
`MigrationService` that writes the result into an empty book in one transaction. The decision
this turns on: **Money encrypts its system catalog in every file, so the catalog is not used
at all** — tables are identified by their column signatures instead, which sidesteps the
encryption entirely.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 11 — foreign files are read-only | The file is opened for reading and never written, moved or renamed. |
| 6 — store nothing you do not need | Account numbers are deliberately not imported. |
| 9 — off-thread, cancellable, transactional | Every stage reports progress; stopping rolls the whole thing back. |
| 8 — no real data in source control | Committed tests assert structural invariants only — never a balance, an account name or a payee. |
| 1 — integer cents | Money's ten-thousandths convert exactly. |

## Technical context

- **Projects:** `MyFinance.Import/Mny` (+ `Mny/Jet`), `MyFinance.Data/Services/MigrationService.cs`.
- **Dependencies:** none. No ODBC, no Microsoft Access component, no Jet driver. It runs
  anywhere .NET does, which is also what makes it testable on Linux.
- **Testing:** `MyFinance.Import.Tests/Mny` for the format; `MyFinance.Data.Tests` for the
  migration, including against the real (gitignored) file when present.

## Design

### Getting around the encrypted catalog

Money encrypts pages 1–14 — its system catalog — in **every** file, password or not
(`MSISAM_MAX_ENCRYPTED_PAGE = 0xE`). An early attempt to derive the RC4 key failed, and then
turned out to be unnecessary: the catalog only tells you which table is which, and that can be
recovered from the tables themselves.

`MoneyTables` identifies each table by the **set of column names** it carries —
`["hacct","szFull","at","amtOpen","fClosed"]` is the account table, `["htrn","hacct","dt",
"amt","grftt","lHpay"]` is transactions, `["sic","hcat"]` is the merchant-code table. If two
tables match one signature, `Find` returns null rather than guessing: a wrong table would
produce a migration that succeeds and is nonsense.

A file with a password set encrypts the *data* pages too, which this approach cannot help
with — hence the explicit report and the instruction to remove the password in Money.

### The format, in the parts that mattered

- 4096-byte pages; page types 0 (database definition), 1 (data), 2 (TDEF), 3/4 (index),
  5 (usage bitmap).
- TDEF layout with the offsets confirmed against the real file: definition length `0x08`,
  row count `0x10`, table type `0x28`, variable columns `0x2B`, column count `0x2D`, real
  index count `0x33`, index block `0x3F`, 12-byte index entries, 25-byte column descriptors.
- A row is read **from both ends**: a 2-byte column count at the front; the null mask, a
  2-byte variable count and *n+1* reversed offsets at the back. The reversed indexing was the
  subtle part — `s = varoffs[vi]; e = varoffs[vi+1]`.
- Compressed unicode, prefixed `FF FE`, switching between one and two bytes per character
  mid-run.
- MEMO fields carry a 12-byte long-value header that must be stripped before decoding —
  without it, text decodes as Chinese.
- Dates are OLE doubles, days from 1899-12-30, clamped to `[-657434, 2958465]` because Money
  writes sentinel dates that would otherwise overflow.
- Currency is int64 in ten-thousandths.

### Flattening the category tree

Money's tree is three levels whose top level is only ever the two roots INCOME and EXPENSE.
That maps onto this application's two levels plus `CategoryKind` with nothing invented and
nothing dropped, which is exactly why two levels was affordable in `003-categories-and-payees`.

### Payee memory is the point

Bringing across every transaction makes the book *complete*. Writing each payee's
`LastCategoryId` and `LastAmount` makes it *useful*: the next imported statement is
categorized from decades of decisions on the first run. Money's merchant-code table comes
across for the same reason — it is the only source in the chain that can categorize a shop the
user has genuinely never dealt with.

### Projected bills, and why the balances look wrong

Money projects scheduled bills forward into the register and **counts them in its balances**.
A book last used a while ago therefore shows a balance far below its real one, because the
bills kept being projected and the salary did not. One account in the development file shows a
balance hundreds of thousands below its real one for exactly this reason, and that is
*correct* for that file — confirmed against the register screenshot, which shows the projected
rows sitting inside Money's own Ending Balance. A figure like that is the expected result of a
faithful migration, not evidence of a bug, and the wizard has to say so or the user will
reasonably conclude the migration failed.

Those rows are kept by default for exactly that reason: leaving them out gives a tidier book
whose figures no longer match Money's, and the verification step depends on the two matching.
The wizard offers the choice and says which is in effect.

### Verification is part of the feature

The last step lists every account with its closing balance, laid out to be read straight
against Money's own account list — because Money is still on the machine and still shows it.
A migration nobody can check is one nobody should trust.

### Alternatives rejected

- **Deriving the RC4 key for the catalog.** Attempted; unnecessary once signatures worked.
- **Requiring Money, or an ODBC/Jet driver.** Ties the feature to a machine that still has a
  discontinued product installed, and makes it untestable in CI.
- **Merging into a non-empty book.** Twenty thousand transactions merged into an existing
  book makes duplicates nobody could untangle.
- **Converting recurring bill definitions.** The frequency codes cannot be verified against a
  single file, and a wrong due date is worse than none. Counted and reported instead.
- **Dropping projected bills by default.** Tidier, and it breaks the only available means of
  verification.

## Project structure

```
src/MyFinance.Import/Mny/Jet/JetDatabase.cs   pages, owners, long-value chains. Never writes.
src/MyFinance.Import/Mny/Jet/JetTable.cs      TDEF parsing, row decoding from both ends
src/MyFinance.Import/Mny/Jet/JetColumn.cs
src/MyFinance.Import/Mny/Jet/JetValues.cs     text, dates, currency
src/MyFinance.Import/Mny/Jet/JetRow.cs
src/MyFinance.Import/Mny/Jet/JetException.cs
src/MyFinance.Import/Mny/MoneyTables.cs       column signatures, ambiguity refused
src/MyFinance.Import/Mny/MoneyReader.cs       the Money-level model
src/MyFinance.Import/Mny/MoneyModels.cs
src/MyFinance.Data/Services/MigrationService.cs   one transaction, empty book only
src/MyFinance.Data/Services/MigrationModels.cs
```

## Risks

- **A file shape not seen here.** This reader was developed against one real file and the
  format documentation. Contained by refusing rather than guessing at every ambiguity, and by
  the row-count check catching a misread table immediately.
- **A password-protected file cannot be read at all.** Reported explicitly with the remedy,
  rather than failing obscurely.
- **The development file predates the reference screenshots**, so the screenshot acceptance
  figures were never validated. Stated in the spec rather than glossed. Correctness rests on
  independent agreement, declared row counts, split sums, transfer cancellation and closed
  accounts netting to zero.
- **A transfer-symmetry test failed on an odd count** during development (it no longer exists under that name) and
  was **not** a reader bug: one 2005 transfer leg is embedded inside a split. The test was
  rewritten to pair legs by (date, |amount|, account pair) including split parts — worth
  recording, because the obvious version of that test is wrong.
