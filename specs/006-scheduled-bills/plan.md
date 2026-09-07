# Implementation Plan: Scheduled bills and the cash-flow forecast

**Spec:** `./spec.md` · **Status:** As-built

## Summary

Recurrence, description and projection are pure functions in `MyFinance.Core`; `ScheduleService`
turns an occurrence into an ordinary register transaction through the normal write path. The
decision this turns on: **an occurrence is computed from the series start and its ordinal,
never from the occurrence before it** — and the schedule stores only what was *done* about a
due date, not the due dates themselves.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 4 — correctness outside WPF | `RecurrenceCalculator`, `RecurrenceDescriber`, `CashFlowForecaster` are all pure and in Core. |
| 2 — signs from the owning account | A bill is negative, a paycheque positive, identically to a hand-entered row. |
| 3 — writes through a service | An entered bill is written by the register path, so it gets every transaction invariant for free. |
| 1 — integer cents | Projections are `Money` sums. |

## Technical context

- **Projects:** `MyFinance.Core/Scheduling`, `MyFinance.Data/Services/ScheduleService.cs`,
  `MyFinance.App/ViewModels/Pages/{Bills,Forecast}PageViewModel.cs`.
- **Types:** due dates are `DateOnly`. A due date is a day in a calendar, not an instant, so
  no clock change or time zone can move it.
- **Testing:** `MyFinance.Core.Tests/Scheduling` for the arithmetic (fast, exhaustive),
  `MyFinance.Data.Tests/Services/ScheduleServiceTests.cs` for entering and auto-entry.

## Design

### Occurrences from the anchor, not from each other

`RecurrenceCalculator` computes the *n*th occurrence as a function of `StartDate` and *n*.
Stepping forward one at a time looks equivalent and is not, in two ways that both compound:

- A bill due on the 31st, clamped to the 28th by one February, would then be anchored at the
  28th and stay there for ever.
- A weekend shift applied to the anchor would push the next occurrence, and the next, until
  the series had drifted a week.

So a shift is applied to the *computed* date, and the computed date is never fed back.

### The schedule stores decisions, not dates

Occurrence history records only due dates that were **entered** or **skipped**. The
recurrence rule remains the single source of truth about when something falls due; the stored
rows exist solely to stop a bill being entered twice. Storing generated due dates would create
a second source of truth that a change to the pattern would immediately contradict — which is
why moving the pattern does not disturb what has already been paid.

### A backlog is entered on the days it was owed

Nine occurrences behind means nine transactions on nine dates, not one for nine times the
amount dated today. The running balance is a statement about history; landing the whole
backlog on today would make every intervening balance wrong.

### The forecast reports its lowest point

`CashFlowForecaster` nets income and outgoings by day and walks the balance forward. The
figure that matters is the **minimum**, not the close: a large bill early in the month
followed by a salary ends comfortably while going overdrawn in between, and the overdraft fee
is real.

### Alternatives rejected

- **Materialising occurrences into a table.** The obvious design, and it makes editing a
  series a data migration. Also creates the second source of truth described above.
- **Stepping date by date.** Drifts, as described. This is the single most common bug in
  recurring-bill implementations.
- **`DateTime` for due dates.** Invites a time zone into a fact that has none.
- **Auto-entering everything due.** Only series that asked for it, so opening the book is
  never a surprise.
- **Deleting the transactions a deleted series produced.** The money left the account
  whatever happened to the schedule.

## Project structure

```
src/MyFinance.Core/Scheduling/RecurrenceRule.cs        the pattern
src/MyFinance.Core/Scheduling/RecurrenceCalculator.cs  occurrences from the anchor
src/MyFinance.Core/Scheduling/RecurrenceDescriber.cs   the reference application's wording
src/MyFinance.Core/Scheduling/CashFlowForecaster.cs    day-by-day projection, low point
src/MyFinance.Core/Entities/ScheduledTransaction.cs
src/MyFinance.Data/Services/ScheduleService.cs         enter, skip, auto-enter, history
src/MyFinance.Data/Services/ScheduleModels.cs
src/MyFinance.App/ViewModels/Pages/BillsPageViewModel.cs
src/MyFinance.App/ViewModels/Pages/ForecastPageViewModel.cs
```

## Risks

- **Double entry** is the failure that costs money. Contained by occurrence history plus
  `Entering_the_same_bill_twice_does_not_record_it_twice` and
  `Running_auto_entry_twice_does_not_double_up`.
- **Auto-entry runs at book open**, so a bug there greets the user before they can do
  anything. Contained by only touching series that asked for it, and by saying what it did.
- **Calendar edge cases are endless.** Contained by testing the specific ones that bite —
  the 31st, the leap day, the weekend shift, twice-monthly with equal days — rather than
  trusting a general argument.
