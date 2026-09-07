# Feature Specification: Bringing recurring bills across from Money

**Folder:** `014-scheduled-bill-migration`
**Created:** 2026-09-05
**Status:** **Implemented** (2026-09-05). The blocker is resolved: the mapping was derived and verified against a restored `.mny`.
**Input:** Identified while retro-specifying `009-money-migration`, which deliberately skips
this and says so.

## Why this is a gap

`009-money-migration` FR-024 records the decision: Money's recurring bill definitions are
**counted and reported but not converted**, because the frequency codes could not be verified
against a single file, and *a wrong due date is worse than no due date*.

That decision was right at the time and it leaves a real cost. A user migrating a book with
decades of history gets every transaction, every payee and every category — and then has to
re-enter every recurring bill by hand under the Bills tab. It is the one place where an
otherwise complete migration hands the user a list of chores.

This is the narrowest and best-understood gap in the list: the target model already exists
(`006-scheduled-bills`), the source rows are already read and counted, and the only missing
piece is a verified mapping of Money's frequency codes.

## User Scenarios & Testing

### Primary user story

The user finishes a migration and finds their bills already listed under the Bills tab, with
the right amounts, the right accounts and the right due dates — or, where a definition could
not be understood, a clear list of the ones they must set up themselves.

### Acceptance scenarios

1. **Given** a Money file with recurring bills, **When** it is migrated, **Then** each
   recognised bill becomes a scheduled transaction with its amount, account, payee, frequency
   and next due date.
2. **Given** a bill whose frequency code is not recognised, **When** the migration completes,
   **Then** it is listed as needing manual setup rather than being converted to a guess.
3. **Given** a migrated bill, **When** its next due date is compared against Money's own bills
   screen, **Then** they agree.

### Edge cases

- **Money's frequency codes turned out to be a pair, not a single value.** `frq` alone is
  ambiguous — the same value covers monthly and twice-monthly series — and `cFrqInst`
  disambiguates. Resolved and verified; see `plan.md`.
- **A bill's row is a revision, not a series.** Money writes a new row sharing one
  `hbillHead` each time the terms change; one bill in the reference file has six. Only the
  newest revision is the bill that exists now.
- **A due date Money never generated is not a payment.** It is marked skipped, not entered.
- A bill whose occurrences were already projected into the register and migrated as
  transactions — it must not then be entered a second time by the new schedule.
- Money's weekend-shift and end-condition settings, if it has them.
- A bill against an account that arrived as balance-only, which cannot hold a schedule
  (`006-scheduled-bills`, FR-022).

## Requirements

### Functional requirements

- **FR-001**: The system MUST convert a recognised Money recurring bill into a scheduled
  transaction with its amount, account, payee, category allocation, frequency and next due
  date.
- **FR-002**: The system MUST NOT convert a definition it cannot map with confidence, and MUST
  list every such definition for the user to set up by hand.
- **FR-003**: The system MUST NOT cause an occurrence already present in the register to be
  entered again.
- **FR-004**: The system MUST report what it converted and what it did not, in the migration
  summary.
- **FR-005**: The system MUST NOT schedule against an account type that cannot hold one.

### Non-functional requirements

- **NFR-001**: A converted bill's computed next due date MUST match Money's own, verified
  against at least one real file before this ships.

## Key Entities

Money's recurring bill rows (already read and counted by `MoneyReader`) mapping onto
`ScheduledTransaction` and its splits (`006-scheduled-bills`).

**This feature introduces no entity of its own**, which is why there is no `data-model.md`
here: the source rows are already read and the target model already exists. The only new
artefact is a mapping table from Money's frequency codes onto `RecurrenceFrequency`, and that
table is the thing the feature is blocked on. It is set out in `plan.md`, where it can be
filled in as evidence arrives.

## Success Criteria

- **SC-001**: For a real Money file, every converted bill's next due date matches Money's
  bills screen.
- **SC-002**: No migrated bill produces a duplicate of a transaction already in the register.
- **SC-003**: Every unconverted definition appears in the report, so the user's manual list is
  complete.

## Assumptions

- Depends on constitution principles **11** (the `.mny` is read-only), **9** (part of the
  migration's transaction and cancellation), and on `006-scheduled-bills` being the target
  model unchanged.
- The blocker is evidence, not design: `NFR-001` cannot be met without at least one real file
  whose bills screen can be compared. That, not implementation effort, is what this feature is
  waiting on.

## Out of Scope

- Inventing a due date where the frequency cannot be determined. That is the failure this
  feature exists to avoid, not a fallback.

## Clarifications

### 2026-09-05

- **Q: Which Money frequency codes exist and what does each mean?** → **Unresolved, and
  deliberately so.** This is an evidence question, not a decision: answering it needs a `.mny`
  together with a view of Money's own Bills screen showing the range of frequencies, so a
  derived mapping can be checked against real due dates. The work is planned *behind* that
  task rather than around it. Guessing a mapping would produce wrong due dates, which `009`
  FR-024 already judged worse than no due dates at all.
- **Q: Should the codes be mapped best-effort with low-confidence rows flagged?** → **No.**
  Same reason. Convert only what is certain; list the rest for manual setup (FR-002).

### Resolution, 2026-09-05

The mapping was derived from a reference `.mny` and verified against Money's own Bills
screen for the same file, three independent ways. Both are local-only and gitignored
(constitution 8), so what is recorded here is the mapping, never the book it came from.
Three frequency pairs cover **every series in that file**:
`(frq 3, cFrqInst 1)` → Monthly, `(frq 3, cFrqInst 2)` → **Twice a month**, `(frq 4, …)` →
Quarterly. Nothing was guessed; the remaining eight frequencies stay unmapped and would be
listed for manual setup.

Three things the plan had not anticipated, each of which would have corrupted the result:

1. **`cFrqInst` is a count per period, not a multiplier.** Passed through as our `Interval`
   it would have turned a twice-monthly salary into a bi-monthly one — halving the largest
   inflow in the book, silently.
2. **A bill is several rows.** Money writes a new row sharing one `hbillHead` whenever the
   terms change, so most rows are earlier revisions. Importing them all created extra copies
   of some bills, each anchored years in the past.
3. **Money carries overdue backlogs and does not auto-enter them.** Its own screen reports
   how many occurrences of a series are past due and waits. Marking only what Money generated
   left the whole backlog to be written as phantom transactions by the first auto-entry after
   a migration.

