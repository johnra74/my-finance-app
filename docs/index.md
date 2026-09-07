# MyFinance

A personal finance application for Windows, in C#/.NET 10 and WPF. It replaces Microsoft
Money Plus Sunset: an encrypted single-user book, an account register, scheduled bills,
budgets and spending reports, fed by the OFX and QIF files your banks let you download.

Version {{ release }}.

:::{admonition} Three things to know before you start
:class: important

1. **There is no password recovery.** The book is encrypted with a key derived from your
   password, which is never stored anywhere. Forgetting it destroys the data. Nobody can
   recover it — not this application, not its author.
2. **A book is two files**, {{ book_ext }} and {{ sidecar_ext }}. Both are needed to open
   it. Losing the sidecar is exactly as fatal as forgetting the password.
3. **Nothing leaves your machine.** No account, no server, no telemetry, no crash
   reports. That is a design rule, not a default — see
   {doc}`dev/principles`.

Read {doc}`user/backups` before you have anything you would mind losing.
:::

::::{grid} 1 1 2 2
:gutter: 3

:::{grid-item-card} {octicon}`person` Using MyFinance
:link: user/index
:link-type: doc

Install it, create a book, bring a Microsoft Money file across, import statements, track
bills, budget, report, print, and keep backups you can actually restore from.
:::

:::{grid-item-card} {octicon}`tools` Working on MyFinance
:link: dev/index
:link-type: doc

The architecture, the eleven principles every change is checked against, the
specification set, building and testing, schema migrations, and cutting a release.
:::

:::{grid-item-card} {octicon}`book` Reference
:link: reference/index
:link-type: doc

File formats, keyboard shortcuts, what a migration does and does not carry, and a
glossary of the terms used throughout.
:::

:::{grid-item-card} {octicon}`shield-lock` How a book is protected
:link: user/security
:link-type: doc

Argon2id, SQLCipher, what the sidecar holds, and the consequences of a design with no
recovery path.
:::

::::

## What it does

| Area | Covered |
|---|---|
| **Accounts and register** | Grouped account list with subtotals, running balance, splits, transfers, reconciliation, void and delete |
| **Statement import** | OFX 1.x and 2.x, QIF, `.qfx`, `.qbo`; account matching, duplicate detection, the sign check, undo |
| **Categorization** | Rules you write, the file's own category, payee memory, a classifier trained on your history, merchant resemblance, industry codes |
| **Bills** | Recurrence from the anchor date, backlogs entered on the days they were owed, auto-entry, a five-month calendar, a cash-flow forecast with its low point |
| **Budgets** | A figure per category per month, surplus rollover, copy across the year |
| **Reports** | Eight reports, each with a chart *and* a table, drill-down to the transactions, CSV export |
| **Migration** | A pure-C# Jet 4 / MSISAM reader for `.mny` files — accounts, categories, payees, transactions, splits, transfers, payee memory, recurring bills and holdings |
| **Investments** | Securities, average-cost holdings and hand-entered prices, counted in net worth with the price date shown |
| **Export** | The whole book as one documented JSON file that needs no MyFinance to read |
| **Backups** | One archive holding both halves of the book, checkpointed, verified by reading it back, pruned |
| **Printing** | Registers and reports, paginated, with the filter named on the page |

```{toctree}
:hidden:
:caption: Using MyFinance

user/index
user/installing
user/first-book
user/accounts-and-register
user/importing
user/categorizing
user/migrating
user/bills
user/budgets
user/reports
user/investments
user/printing
user/exporting
user/backups
user/security
user/troubleshooting
```

```{toctree}
:hidden:
:caption: Working on MyFinance

dev/index
dev/architecture
dev/principles
dev/specifications
dev/building
dev/testing
dev/schema-migrations
dev/releasing
dev/documentation
```

```{toctree}
:hidden:
:caption: Reference

reference/index
reference/file-formats
reference/keyboard
reference/migration-coverage
reference/glossary
```
