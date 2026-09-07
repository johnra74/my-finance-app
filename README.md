# MyFinance

A personal finance application for Windows, in C#/.NET 10 and WPF. It replaces Microsoft
Money Plus Sunset: an encrypted single-user book, an account register, scheduled bills,
budgets and spending reports, fed by the OFX and QIF files your banks let you download.

**📖 Full documentation: [`docs/`](docs/index.md)** — build it with the steps under
[Documentation](#documentation) below, or read the Markdown directly.

---

## Three things to know first

1. **There is no password recovery.** The book is encrypted with a key derived from your
   password, which is never stored anywhere. Forgetting it destroys the data.
2. **A book is two files**, `.mfdb` and `.mfmeta`. Both are needed to open it. Losing the
   sidecar is exactly as fatal as forgetting the password.
3. **Nothing leaves the machine.** No telemetry, no crash reporting, no update check, no
   network calls of any kind.

See [How a book is protected](docs/user/security.md) and [Backups](docs/user/backups.md).

## What it does

| Area | Covered |
|---|---|
| **Accounts and register** | Grouped account list with subtotals, running balance, splits, transfers, reconciliation, void and delete |
| **Statement import** | OFX 1.x and 2.x, QIF, `.qfx`, `.qbo`; account matching, duplicate detection, the sign check, undo |
| **Categorization** | Rules you write, the file's own category, payee memory, a classifier trained on your history, merchant resemblance, industry codes |
| **Bills** | Recurrence from the anchor date, backlogs entered on the days they were owed, auto-entry, a five-month calendar, a cash-flow forecast with its low point |
| **Budgets** | A figure per category per month, surplus rollover, copy across the year |
| **Reports** | Eight reports, each with a chart *and* a table, drill-down, CSV export |
| **Migration** | A pure-C# Jet 4 / MSISAM reader for `.mny` files — accounts, categories, payees, transactions, splits, transfers, payee memory, recurring bills and holdings |
| **Investments** | Securities, average-cost holdings, hand-entered prices, counted in net worth with the price date shown *(engine only — no screen yet)* |
| **Export** | The whole book as one documented JSON file that needs no MyFinance to read |
| **Backups** | One archive holding both halves of the book, checkpointed, verified by reading it back, pruned |
| **Printing** | Registers and reports, paginated, with the filter named on the page |

## Layout

```
src/MyFinance.Core       net10.0           domain model, Money, register and balance logic,
                                           recurrence, cash-flow projection, the report
                                           engine, budget arithmetic, chart geometry,
                                           holding arithmetic, diagnostics, threading
src/MyFinance.Data       net10.0           EF Core + SQLCipher, schema, migrations, services
src/MyFinance.Import     net10.0           OFX and QIF parsing, payee cleanup, dedupe, sign
                                           analysis, rules, category suggestion, and the
                                           Jet 4 / MSISAM reader for .mny files
src/MyFinance.Semantics  net10.0           the local embedding model, isolated so the other
                                           projects stay pure and quick to test
src/MyFinance.App        net10.0-windows   WPF shell, views, view models
tests/                   net10.0           xUnit + Shouldly
```

Only `MyFinance.App` targets Windows. Everything else is platform-neutral and testable
anywhere — deliberately, because it keeps the logic that has to be *correct* out of the layer
that can only be verified by eye.

See [Architecture](docs/dev/architecture.md).

## Building

**Windows** — the only platform where the WPF application actually runs:

```
build            :: build + test  (the default)
build run        :: launch the app
build publish    :: self-contained single-file .exe in artifacts\publish
```

**Linux / macOS** — engine work and CI. The WPF project compiles but cannot run:

```
./build.sh          # build + test
./build.sh publish  # a Windows .exe, cross-built
```

Prerequisite is the .NET 10 SDK. Full detail, including the PowerShell execution-policy shim
and the EF migration commands, is in [Building](docs/dev/building.md),
[Testing](docs/dev/testing.md) and [Schema migrations](docs/dev/schema-migrations.md).

## Documentation

Sphinx + MyST, published to Read the Docs. Every page is Markdown, so text moves between
`specs/` and `docs/` without translation.

```bash
python3 -m venv .venv-docs
.venv-docs/bin/pip install -r docs/requirements.txt
.venv-docs/bin/sphinx-build -W -b html docs docs/_build/html
```

`-W` turns warnings into errors, as Read the Docs does — a broken cross-reference fails the
build rather than shipping. See [Writing the documentation](docs/dev/documentation.md).

## Specifications and principles

`specs/` holds eighteen numbered specifications written to
[GitHub Spec-kit](https://github.com/github/spec-kit) conventions, and
`.specify/memory/constitution.md` holds the eleven principles every one of them is checked
against.

> Where the code and a specification disagree, the code is the fact and the specification is
> the bug.

See [The eleven principles](docs/dev/principles.md) and
[The specification set](docs/dev/specifications.md), which also lists the known gaps.

## Data in this repository

**Real financial data never enters source control.** `.gitignore` excludes every book,
backup, statement and Money file; the only exceptions are the redacted fixture folders
`tests/fixtures/ofx/redacted/` and `tests/fixtures/qif/redacted/`. Tests that need a real
`.mny` skip when none is present and assert only structural invariants — never a balance, an
account name or a payee.

The embedding model's weights are not committed either. `tools/fetch-model.sh` — or
`tools\fetch-model` on Windows — downloads them and checks them against pinned hashes.

## Licence

Apache License 2.0 — see [`LICENSE`](LICENSE). What the published executable carries, and
under what terms, is in [`NOTICE`](NOTICE); the reasoning behind the choice is in
[Licensing](docs/dev/licensing.md).

The Money reader in `MyFinance.Import.Mny` is original work, not a port of mdbtools or
Jackcess, so it carries no copyleft and can be reused anywhere.

Microsoft, Microsoft Money and Windows are trademarks of Microsoft Corporation. MyFinance is
not affiliated with or endorsed by Microsoft; it names those products to describe what it
reads and what it runs on.
