# The specification set

`specs/` holds eighteen numbered specifications written to
[GitHub Spec-kit](https://github.com/github/spec-kit) conventions: one folder per feature,
`spec.md` for *what and why*, `plan.md` for *how*, `tasks.md` for the work and its
traceability, and `data-model.md` where the feature owns data.

## The rule that makes them worth keeping

> **Where the code and a specification disagree, the code is the fact and the specification
> is the bug.**

## Conventions

- `spec.md` contains **no technology** — no class names, no file paths, no libraries. Those
  belong in `plan.md`.
- Requirements are `FR-001…`, non-functionals `NFR-001…`, success criteria `SC-001…`, and
  success criteria are **measurable** and cite the test that proves them.
- An open question is `[NEEDS CLARIFICATION: the question]` and is **never quietly resolved
  by a guess**. Every one that is answered is recorded in that spec's Clarifications section,
  marked as asked or assumed.
- Every specification names, under **Assumptions**, the principles it depends on.

## What is specified

| # | Specification | State |
|---|---|---|
| 001 | The encrypted book | Implemented |
| 002 | Accounts and the register | Implemented |
| 003 | Categories and payees | Implemented |
| 004 | Importing a bank statement | Implemented |
| 005 | Recommending a category | Implemented |
| 006 | Scheduled bills and the forecast | Implemented |
| 007 | Budgets | Implemented |
| 008 | Reports, charts and the dashboard | Implemented |
| 009 | Bringing a Money book across | Implemented |
| 010 | Backup and restore | Implemented |
| 011 | Packaging and release | Implemented |
| 012 | Exporting the whole book | Implemented |
| 013 | Investment accounts | Implemented — engine only, no screen |
| 014 | Recurring bills from Money | Implemented |
| 015 | More than one currency | **Partly implemented** — persistence deferred |
| 016 | Printing | Implemented |
| 017 | Opening a book from another version | Implemented |
| 018 | A diagnostics log | Implemented |

Specifications 001–011 were written **from the shipped code**, after the fact. Their
`tasks.md` is a traceability matrix rather than a plan: every task carries the file that
implements it and the test that proves it, and where something is untested the coverage
table says so with a ⚠️ rather than a tick.

## The commands

Run through the slash commands in `.claude/commands/`:

| Command | |
|---|---|
| `/speckit.specify <description>` | Write a new numbered specification |
| `/speckit.clarify <folder>` | Resolve the open questions in one |
| `/speckit.plan <folder>` | Turn a clarified specification into a plan |
| `/speckit.tasks <folder>` | Break a plan into traceable tasks |
| `/speckit.analyze [folder]` | Check the set for drift against the code |

## Known gaps

Tabled in `specs/README.md`, ranked by what it costs the user not to have them. The ones
with the worst consequence today:

- **Two copies open on one book.** Nothing prevents it. The upgrade path moves files and the
  password change rewrites the sidecar; either, run while a second instance holds the book,
  loses data in a file with no recovery path.
- **No way back in from the export.** The whole book exports; nothing reads it back.
- **Investments have no screen.** The service is complete and consumed by nothing.
- **No integrity check.** Nothing tells you the book is corrupt, or which backup is the last
  good one.
- **Scheduled transfers** cannot be represented.

Plus the longer-standing list: CSV statement import, cross-account search, attachments, tax
lines and a tax report, loan and mortgage amortization, an accessibility conformance
statement, an update path, and an idle lock.
