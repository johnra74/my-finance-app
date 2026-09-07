# Implementation Plan: Bringing recurring bills across from Money

**Spec:** `./spec.md` · **Status:** **Built** — see `./tasks.md`

## Summary

Read Money's recurring bill definitions — already reached and counted by `MoneyReader` — and
convert each one into a `ScheduledTransaction` inside the existing migration transaction. The
work is small; the **blocker is a verified mapping from Money's frequency codes onto
`RecurrenceFrequency`**, which cannot be derived from one file and is not guessed.

This feature introduces no new entity, so there is no `data-model.md`. The only new artefact
is the mapping table below.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 11 — foreign files are read-only | Reads the `.mny`; writes only into the new book. |
| 9 — transactional, cancellable | Runs inside `MigrationService`'s existing transaction and cancellation, adding a stage rather than a second pass. |
| 3 — writes through a service | Bills are written through `ScheduleService`, so they arrive with the same invariants a hand-entered series has. |
| 5 — a guess is never applied unseen | The whole design: convert what is certain, list the rest. |

## Technical context

- **Projects:** `MyFinance.Import/Mny` (read the rows), `MyFinance.Data/Services/MigrationService.cs`
  (a new stage).
- **Everything downstream already exists and is reused unchanged:**
  - `MoneyReader` / `MoneyTables` — already locate and count these rows; the table signature
    work is done.
  - `RecurrenceRule` and `RecurrenceCalculator` (`MyFinance.Core/Scheduling`) — the target
    model, including the anchor-based occurrence rule and weekend shifts.
  - `ScheduleService` — the write path, including the refusal to schedule against a
    balance-only account.
  - `MigrationService` — the transaction, the progress reporting and the summary this stage
    reports into.
- **Testing:** `tests/MyFinance.Import.Tests/Mny/` for the mapping;
  `tests/MyFinance.Data.Tests/Services/MigrationServiceTests.cs` for the conversion.

## The blocker, stated precisely

Money stores a frequency as a code. **This codebase has one `.mny` file, and one file exercises
only the frequencies that user happened to use.** A code seen once, in one series, with one
observed due date, is consistent with several meanings — "every four weeks" and "monthly" agree
for months at a time, and "twice a month" and "every two weeks" agree eleven times a year.

`009-money-migration` FR-024 already recorded the resulting decision: count these rows, report
them, and **do not convert them**, because a wrong due date is worse than no due date. That
judgement stands. This plan does not overturn it; it sets out what would.

### What would unblock it

A `.mny` file **together with a view of Money's own Bills screen** for the same file, showing
each series with the frequency Money itself displays. That pairing is what turns a code into a
meaning: the file gives the code, the screen gives the label, and `RecurrenceCalculator` can
then be checked to produce the same next due date Money shows.

One file covering few frequencies is partial evidence and is still useful — it fixes those
codes and leaves the rest unmapped, which is exactly the shape FR-002 expects.

### The evidence, and what it turned out to say

**Resolved on 2026-09-05** against a reference `.mny` plus Money's own Bills summary for
the same file. Both are gitignored and stay on the machine that has them (constitution 8);
only the mapping below is recorded.

The plan assumed `frq` was the frequency. **It is not.** The frequency is the pair
`(frq, cFrqInst)`, and `cFrqInst` is a **count per period, not a multiplier**:

| `frq` | `cFrqInst` | Observed median gap | Money's own label | Ours |
|---|---|---|---|---|
| 3 | 1 | 30–35 days | *Monthly* | `Monthly` |
| 3 | **2** | **15 days** | *Twice a month* | `TwiceAMonth` |
| 4 | 1 | 92 days | *Every three months* | `Quarterly` |

Every series in the reference file fell into one of those three. How many of each, and what
they were for, is the owner's business and is not recorded here.

**`cFrqInst = 2` means twice per month, not every two months.** Our `Interval` means the
opposite — `Interval = 2` with `Monthly` is *every two months*. Mapping one onto the other
would have turned a twice-monthly salary into a bi-monthly one: half the income, silently,
where that salary is the largest single inflow. This is precisely the error
`009-money-migration` FR-24 refused to risk, and it would not have been caught by reading the
codes alone.

Each row is corroborated three ways, which is the bar `NFR-001` sets:

1. **Money's own label** for one series of each kind — a mortgage shown as *Monthly*, a salary
   deposit as *Twice a month*, a water utility as *Every three months*.
2. **Observed spacing** of the instances Money actually generated: 226 instances at a 15-day
   median for the twice-monthly salary, 109 at 31 days for the mortgage, 23 at 92 days for the
   water bill.
3. **Mutual exclusivity** across all 145 datable series — `cFrqInst = 2` produces a 15-day gap
   and nothing else; `frq = 4` produces 92 and nothing else.

### A trap worth naming: `cFrqInst` is a `Double`

`JetRow.Int("cFrqInst")` returns **null**, because `Int()` handles `int`, `short`, `byte` and
`long` — and this column is stored as a floating-point value. It reads as "no interval set" on
every row, silently.

An implementation that used the obvious accessor would treat every series as `cFrqInst`-less,
map them all to plain monthly, and produce exactly the wrong-by-half salary described above —
with no error, no warning, and nothing in the migration report to suggest anything had gone
wrong. `MoneyFrequency` must read it with `Double()`, and a test pins that.

### What this covers

Three of the eleven frequencies — and **all ten** bills on the user's Bills screen, and all 145
datable series in the file. The other eight codes have never been seen and stay unmapped,
falling to FR-002 exactly as designed.

## Design

### A stage in the existing migration, not a second pass

The conversion runs inside `MigrationService`'s existing transaction, after transactions and
transfers, as one more reported stage. Cancelling still rolls the whole thing back, and the
user still sees one operation rather than two.

### Convert only what is certain

A definition whose code has a verified mapping becomes a `ScheduledTransaction`. Everything
else is listed by name, amount and account in the migration summary, so the user's manual list
is complete and finite — which is materially better than today, where they must find the bills
themselves.

### Do not re-enter what is already in the register

Money projects bills forward into its register and `009` keeps those rows by default. So a
migrated book may already contain transactions for occurrences that the new schedule would
consider outstanding. The occurrence history that `006-scheduled-bills` FR-018 records exists
for exactly this: each converted series is seeded with **entered** markers for the due dates
whose transactions came across, so auto-entry on first open does not pay every bill a second
time.

This is the single most dangerous thing in the feature, and it is why T005 is not optional.

### Balance-only accounts

A Money loan or investment account arrives as `AccountType.UnsupportedImported`, which
`ScheduleService` already refuses as a schedule target (`006`, FR-022). Such definitions go on
the manual list with a reason, rather than failing the migration.

### Alternatives rejected

- **Best-effort mapping with low-confidence rows flagged.** Considered and declined by the
  user. A flagged wrong due date is still a wrong due date, and the flag is gone the moment
  the user dismisses it.
- **Inferring the frequency from the spacing of already-entered occurrences.** Attractive, and
  it fails on exactly the cases that matter: a series with one or two entries, and the
  monthly/four-weekly pair.
- **Asking the user to confirm each frequency during migration.** Turns a migration into a
  questionnaire with one question per bill, at the moment the user has least context.
- **A separate post-migration import step.** Would need its own transaction, its own
  duplicate handling and its own cancellation, all of which `MigrationService` already has.

## Project structure

```
src/MyFinance.Import/Mny/MoneyModels.cs        + the recurring-bill row shape
src/MyFinance.Import/Mny/MoneyReader.cs        read the rows (already located and counted)
src/MyFinance.Import/Mny/MoneyFrequency.cs     NEW — the mapping, and "unknown" as a real answer
src/MyFinance.Data/Services/MigrationService.cs   + a bills stage
src/MyFinance.Data/Services/MigrationModels.cs    + converted and unconverted counts
tests/MyFinance.Import.Tests/Mny/MoneyFrequencyTests.cs        NEW
tests/MyFinance.Data.Tests/Services/MigrationServiceTests.cs   extended
```

## Risks

- **The evidence may never arrive**, and then this stays unbuilt. That is an acceptable
  outcome and a better one than a book full of wrong due dates; the spec exists so the
  decision is visible rather than forgotten.
- **Partial evidence is likely** — one file will not cover every frequency. The design absorbs
  it: unmapped codes fall to the manual list, and the mapping table grows.
- **Double payment is the expensive failure.** A converted series that does not know its
  occurrences were already entered would pay every backdated bill again on first open.
  Contained by T005, and by `006`'s existing occurrence history rather than new machinery.

## What changed during implementation

Four discoveries, each caught by a test rather than by review, and each of which would have
produced a wrong book.

1. **`cFrqInst` is a count per period, not a multiplier** — the finding recorded above. Our
   `Interval` means the opposite, and passing one through as the other halves a salary.
2. **`hbillHead` is not a transaction id.** It is a series key stamped on every generated
   instance. The bill's template is `lHtrn`. Following `hbillHead` as a transaction id
   resolved to an unrelated row for almost every bill, putting an everyday shop's name on a
   bill that was nothing of the kind. Caught by reading the
   output against Money's own Bills screen, which is the whole reason `009` insists that step
   exists.
3. **One bill is several rows.** Money writes a new row sharing one `hbillHead` whenever a
   bill's terms change, with `iinst` marking the instance the new terms take effect from — so
   most rows in the table are superseded revisions. Importing every row created one active
   copy per revision, all but the newest anchored years in the past, each back-filling a
   decade of occurrences.
4. **A due date Money never generated is not a payment.** Marking only the occurrences Money
   actually produced left the rest outstanding, and the first auto-entry after a migration
   wrote a backlog of transactions that never happened. Money does not do this — it lists
   them as overdue and waits, which is what *"n occurrences past due"* on its own screen
   means. Those
   occurrences are now marked **skipped**: due, unpaid, and deliberately not invented.

### A separate defect, found and not fixed here

The row-count invariant failed on the restored file:
**one table reads one row short**. The cause is a row stored on an **overflow page**, which
`JetTable.Rows()` skips along with deleted rows rather than following.

The affected table is Money's advice/alerts table, which nothing in this application reads, so
the impact here is nil — but the defect is real, and on a table that *is* read it would be
silent data loss. It is Jet work in its own right rather than something to fold into an
unrelated change. The check has been split so the strict, no-exceptions assertion now covers
**every table the migration actually reads**, and a second test still reports any *new* short
read anywhere else. Deleting the check because one table failed it would have thrown away the
canary that found the defect.

