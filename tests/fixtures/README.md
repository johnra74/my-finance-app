# Test fixtures

## Nothing here is real

Every account name, payee, merchant and figure in the test suite is invented — the
sample-database cast (Contoso, Fabrikam, Northwind, Adventure Works, Woodgrove) precisely
because nobody banks with them. A committed test that asserts a real balance publishes it
permanently, and one that names a real account publishes which bank someone uses.

The whole invented book — accounts, categories, payees, four years of transactions with
splits, transfers, bills and holdings — is `SampleBook` in
`tests/MyFinance.Data.Tests/Fixtures`. Build from it rather than from anything real.

## Bank statements — read this before adding a file

A raw `.ofx` / `.qfx` / `.qbo` download from your bank contains, in plain text:

- your **full account number** (`ACCTID`) and the bank's **routing number** (`BANKID`)
- every merchant you paid, with dates and amounts
- your closing balance

`.gitignore` excludes those extensions everywhere **except** `ofx/redacted/`. That exception
is deliberately narrow: it means a raw download can never be committed by an absent-minded
`git add -A`, because the only directory git will accept one from is the one whose name says
it has been through the redactor.

### Where files go

```
tests/fixtures/ofx/private/    real downloads — gitignored, never leave your machine
tests/fixtures/ofx/redacted/   redactor output — committed, used by the golden tests
```

### Producing a redacted copy

`OfxRedactor` (in `MyFinance.Import.Tests/Fixtures`) rewrites a real file in place of its
identifying parts while leaving everything a parser test cares about byte-identical:

| Rewritten | Preserved |
|---|---|
| `ACCTID`, `BANKID`, `ACCTKEY` | tag order, nesting, unclosed tags |
| `FITID` (stable hash, so duplicate detection still works) | encoding and line endings |
| `NAME`, `MEMO`, `PAYEE` (fictional merchants) | header block, malformations |
| `ORG`, `FID` | **amounts**, and their relationship to `LEDGERBAL` |
| dates, optionally shifted by a constant | |

Run `OfxRedactorTests.Redact_the_private_fixtures` with the environment variable
`MYFINANCE_REDACT=1` set. It reads everything in `private/` and writes twins into
`redacted/`.

### What a redacted file still reveals

**Amounts and their dates survive**, because the ledger-balance cross-check — the thing that
catches a statement being imported with every sign reversed — is only testable if the
amounts still add up to the stated closing balance. A redacted file therefore still shows
*how much* you spent and *when*, just not *where* or *from which account*.

If that is more than you want in a repository, use `--shift-dates` and accept that the
balance-reconciliation fixture will not be committed; the synthetic fixtures in
`OfxSamples.cs` already cover that path.

**Look at the redacted output before you commit it.** The redactor handles the fields banks
are supposed to use; a bank that puts something identifying somewhere unusual will not be
caught automatically.
