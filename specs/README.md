# Specifications

Written with [GitHub Spec-kit](https://github.com/github/spec-kit) conventions: one numbered
folder per feature, `spec.md` for *what and why*, `plan.md` for *how*, `tasks.md` for the work
and its traceability, `data-model.md` where the feature owns data.

The cross-cutting rules everything here is checked against are in
[`.specify/memory/constitution.md`](../.specify/memory/constitution.md).

**Specs 001–011 were written from the shipped code**, after the fact. Their `plan.md` records
the design that was actually chosen and the alternatives rejected; their `tasks.md` is a
**traceability matrix** — every task marked complete, each carrying the file that implements
it and the test that proves it. Where something is untested, the coverage table says so with a
⚠️ rather than a tick.

**Specs 012–019 were written for gaps in the product**, and all but `015` have since been
built. Each carries a `plan.md` and a `tasks.md`, and every open question is answered and
recorded in that spec's Clarifications section, marked as asked or assumed — so what was
decided, and by whom, is readable afterwards rather than re-derived.

## Commands

| Command | What it does |
|---|---|
| `/speckit.specify <description>` | Write a new numbered spec |
| `/speckit.clarify <folder>` | Resolve the open questions in one |
| `/speckit.plan <folder>` | Turn a clarified spec into a plan |
| `/speckit.tasks <folder>` | Break a plan into traceable tasks |
| `/speckit.analyze [folder]` | Check the spec set for drift against the code |

## Shipped

| # | Spec | Covers | Primary code |
|---|---|---|---|
| 001 | [The encrypted book](001-encrypted-book/spec.md) | Create, open, unlock, change password; the `.mfdb` + `.mfmeta` pair; no recovery | `Data/Security/` |
| 002 | [Accounts and the register](002-accounts-and-register/spec.md) | Account list and subtotals; register and running balance; splits; transfers; reconcile | `Core/{Registers,Accounts,Validation}`, `Data/Services/{Account,Register,Reconcile}Service.cs` |
| 003 | [Categories and payees](003-categories-and-payees/spec.md) | Two-level chart, archive and merge; payees, aliases, normalization | `Core/Payees`, `Data/Services/{Category,Payee}Service.cs` |
| 004 | [Importing a bank statement](004-statement-import/spec.md) | OFX 1.x/2.x, QIF, account matching, dedupe, the sign check, undo | `Import/{Ofx,Qif,Payees,Dedupe}`, `Data/Services/ImportService.cs` |
| 005 | [Recommending a category](005-category-suggestion/spec.md) | The six-source chain, rules, classifier, local embeddings, SIC codes | `Import/Categorization`, `Semantics`, `Data/Services/SuggestionService.cs` |
| 006 | [Scheduled bills and the forecast](006-scheduled-bills/spec.md) | Recurrence from the anchor, backlogs, auto-entry, cash flow and its low point | `Core/Scheduling`, `Data/Services/ScheduleService.cs` |
| 007 | [Budgets](007-budgets/spec.md) | A figure per category per month, rollover of surplus only, copy across the year | `Core/Budgeting`, `Data/Services/BudgetService.cs` |
| 008 | [Reports, charts and the dashboard](008-reports-and-dashboard/spec.md) | Eight reports, chart *and* table, drill-down, the uncategorized warning, CSV | `Core/Reporting`, `Data/Services/ReportService.cs` |
| 009 | [Bringing a Money book across](009-money-migration/spec.md) | A pure-C# Jet 4 / MSISAM reader; three levels into two; payee memory; read-only | `Import/Mny`, `Data/Services/MigrationService.cs` |
| 010 | [Backup and restore](010-backup-and-restore/spec.md) | `.mfbak` holding both halves, checkpointed, verified, pruned; restore before opening | `Data/Security/BackupService.cs`, `Data/Services/BookBackupService.cs` |
| 011 | [Packaging and release](011-packaging-and-release/spec.md) | One self-contained file, native libraries, no trimming, cross-built from Linux | `build.*`, `Directory.Build.props`, `App/app.manifest` |

## Gaps — seven built, one part-built

Ranked by what it costs the user not to have it. The **Build order** column is the sequence
these should be taken in — by consequence and by what blocks what, not by number.

| # | Spec | Build order | The gap, in one line |
|---|---|---|---|
| 012 | [Exporting the whole book](012-full-book-export/spec.md) | ✅ **built** | **Constitution principle 10 is only half kept.** Reports export; the book does not. A MyFinance book is readable only from inside MyFinance — materially the situation Money left this user in. |
| 013 | [Investment accounts](013-investment-accounts/spec.md) | ✅ **built** | The largest functional hole against Money. A brokerage account arrives balance-only and read-only, so net worth — the headline figure on the dashboard — is incomplete. |
| 014 | [Recurring bills from Money](014-scheduled-bill-migration/spec.md) | ✅ **built** | A migration used to hand the user a list of bills to re-enter by hand. All 46 series in the reference file now come across, with their frequencies verified against Money's own Bills screen rather than guessed. |
| 015 | [More than one currency](015-multi-currency/spec.md) | ◐ **part-built** | `Account.CurrencyCode` is a promise the arithmetic does not keep: `Money` has no currency, so two would silently add. Needs deciding **before** a second currency is allowed in. |
| 016 | [Printing](016-printing/spec.md) | ✅ **built** | Nothing prints. CSV plus a spreadsheet covers reports; it does not cover a register somebody wants on paper. |
| 017 | [Opening a book from another version](017-book-schema-upgrade/spec.md) | ✅ **built** | The worst consequence in the list: one encrypted file, often the only copy, no recovery. A newer book opened by an older build is not even refused today. Smallest of the six, and it follows a precedent the sidecar already sets. |
| 018 | [A diagnostics log](018-diagnostics-log/spec.md) | ✅ **built** | **Nothing writes a log.** `Microsoft.Extensions.Logging` is referenced and never called. Two of the three ways an exception can escape are unhooked, and 20+ fire-and-forget calls drop theirs in silence. Raised by a live fault nobody can investigate. |
| 019 | [In-application help](019-in-application-help/spec.md) | ✅ **built** | **The documentation was unreachable from the application.** No Help, no `F1`, no About — so the written answers to the questions with the worst consequences (the password, the second file, the unencrypted export) sat in a folder no user knew about. |

## Gaps — tabled, no draft yet

| Gap | What it costs today |
|---|---|
| **CSV statement import** | Banks that offer no OFX or QIF cannot be imported at all, so those accounts are hand-entered for ever. The parsing infrastructure exists; only a column-mapping step is missing. |
| **Cross-account search** | Search is per-register (`002`, FR-015). "Where did I pay that plumber?" needs the user to know the account first — which is exactly what they are trying to find out. |
| **Attachments and receipts** | No way to keep a receipt or an invoice against a transaction. The memo is the only place, and it is text. |
| **Tax-line assignment and a tax report** | `Category.IsTaxRelated` is a boolean and nothing maps a category to a tax form line. At tax time the user exports and sorts it themselves. |
| **Loan and mortgage accounts** | `AccountType.UnsupportedImported` covers these too. No amortization, no principal/interest split, so a mortgage payment is one lump against a category. |
| **Accessibility conformance** | Charts always ship with a table and status colours are always written out (`008`), which is the substance — but there is no statement of keyboard-only, screen-reader or high-contrast support, and no way to check it without Windows. |
| **An update path** | A single copyable `.exe` with no updater and nothing that says a new version exists. Every upgrade is a manual file copy — which is also what makes `017` reachable. |
| **Idle lock / re-prompt** | The book stays open for the whole session. Nothing re-asks for the password, so an unlocked book on an unattended machine is readable. Out of scope in `001` today, deliberately, but unstated as a decision. |

## Conventions used here

- `spec.md` contains **no technology** — no class names, no file paths, no libraries. Those go
  in `plan.md`.
- Requirements are `FR-001…`, non-functionals `NFR-001…`, success criteria `SC-001…` and
  **measurable**.
- Open questions are `[NEEDS CLARIFICATION: the question]` and are never quietly resolved by a
  guess.
- Every spec names, under **Assumptions**, the constitution principles it depends on.
- **Where the code and a spec disagree, the code is the fact and the spec is the bug.**

### What the six plans assume

Every decision taken while planning them is recorded in that spec's **Clarifications**
section, marked as asked or assumed. The load-bearing ones:

- **012** — one JSON document, covering *everything* including bills, budgets and rules;
  plaintext, with the warning in the sentence the user reads before choosing a location. A
  matching importer is a separate future spec. **Built.** Constitution principle 10 is now
  fully kept — the note under it in the constitution should be revisited.
- **013** — average cost, not lots; hand-entered prices with the price date shown beside every
  valued figure; no corporate actions in a first version. **Built**, with one requirement it
  could not meet: **Money records no cost basis** this reader can recover, so a migrated
  holding brings its quantity and price history and reports the missing cost rather than
  importing a zero that would read as free.
- **015** — **refuse to total unlike currencies** rather than converting. No rate table, and
  therefore no staleness and no historical-rate policy. **Part-built**: `Money` now carries its
  currency and refuses mixed arithmetic, and totals are reported per currency — but
  *persistence is deferred*, so a second currency still cannot enter a book. The spec assumed a
  sibling column was small; EF's value converter cannot carry two columns into one struct, and
  the fork is left for whoever needs a foreign account. See that spec's closing section.
- **016** — registers and reports only; **no cheque printing**. **Built**, with six of its
  eleven coverage rows carrying a ⚠️: three need a person with a printer, composition still
  runs on the UI thread (only spooling is async), and nothing yet lets the user *set* the
  column selection the fitter honours.
- **014** — convert only what is verified; list the rest. **Built**, and it turned up three
  traps worth knowing about: Money's `cFrqInst` is a count per period rather than a multiplier,
  a bill is several rows of which most are superseded revisions, and a due date it never generated
  must be marked *skipped* rather than left outstanding — otherwise the first auto-entry writes
  a decade of phantom payments.
- **017** — a newer book is **refused**, not opened read-only; the schema version lives in both
  the database and the sidecar. **Built.** One departure worth knowing about: all-or-nothing
  turned out to need copy-and-replace rather than a transaction, because EF Core cannot wrap a
  *sequence* of migrations. `015` and `013` both add migrations and must bump
  `BookSchema.Current`.

### One decision in 018 worth knowing

The log's API takes an **enum**, not a string. With 59 `catch` blocks in the solution, a rule
each of them must remember is a rule that will be broken — so the writer offers no parameter
that could carry a payee, an amount or a path, and redaction is something the compiler
enforces. That is also why `Microsoft.Extensions.Logging` was **removed** rather than used: its
whole shape is `LogError("failed for {Payee}", payee)`.

### What is left

- **`015`'s persistence.** The type-level work is done and a second currency remains
  impossible, so nothing is at risk; the schema fork is recorded and waits for whoever needs a
  foreign account.
- **`013`'s holdings screen.** The service returns everything a view needs; no view consumes it
  yet, and it is the one part that needs Windows to check at all.
- **Everything only a person can check** — `016`'s printed output, three of whose eleven
  coverage rows need somebody with a printer, and `018`'s three exception hooks, which are four
  lines each but which nothing here proves actually fire.

