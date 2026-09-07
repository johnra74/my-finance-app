# Implementation Plan: Budgets

**Spec:** `./spec.md` · **Status:** As-built

## Summary

`BudgetCalculator` in `MyFinance.Core` takes budget lines and actual spending and produces
the meter figures, including the rollover chain. `BudgetService` stores and copies figures.
The decision this turns on: **actual spending comes from the report engine**, not from a
second query written for this screen, so a budget and a spending report can never disagree.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 4 — correctness outside WPF | `BudgetCalculator` is pure; the meter is drawn from figures already computed. |
| 1 — integer cents | Remainders and rollovers are `Money`; nothing is a percentage of a float. |
| 3 — writes through a service | `BudgetService`. |

## Technical context

- **Projects:** `MyFinance.Core/Budgeting`, `MyFinance.Data/Services/BudgetService.cs`,
  `MyFinance.App/ViewModels/Pages/BudgetPageViewModel.cs`.
- **Testing:** `BudgetCalculatorTests` for the arithmetic; `ReportServiceTests` for the
  agreement with spending, including over the real migrated book.

## Design

### Stored positive, always

A budget line's amount is positive whatever the caller supplied and whatever kind the category
is. Spending is negative in the register (constitution 2), and mixing the two conventions in
one table is how a budget screen ends up showing $-400 remaining out of $-400. The sign
convention is applied once, at the comparison.

### Rollover carries surplus, never debt

An unspent remainder is added to next month's figure when the category asks for it. An
overspend is **not** carried: next month starts from the figure that was set. Carrying a debt
forward would make a single bad month cascade into a year of impossible budgets, and the user
would stop believing the screen — which is the actual failure mode of a budgeting tool.

Because the chain depends on every prior month, it is computed from where the rollover began,
not from the start of the displayed window. Showing March alone must produce the same
remaining figure as showing January to March.

### One source of spending

`BudgetCalculator` consumes the same grouped totals `ReportEngine` produces. A separate query
would eventually differ over a detail — voided rows, transfers, split handling — and the user
would have two numbers for one fact with no way to tell which is right.

### Copy across the year

A budget is mostly the same number twelve times, and typing it out twelve times is how people
give up on budgeting. Months that already have a figure are left alone by default: the copy is
a convenience, not an overwrite.

### Alternatives rejected

- **Storing the remaining figure.** It is a function of the budget and the spending, both of
  which change. A second source of truth.
- **Carrying an overspend forward.** Realistic in an accounting sense, useless as a tool.
- **Clamping an overspend to zero remaining.** Hides the thing the user most needs to see.
- **A separate spending query for the budget screen.** See "one source of spending".

## Project structure

```
src/MyFinance.Core/Budgeting/BudgetCalculator.cs   meters, remainders, the rollover chain
src/MyFinance.Core/Entities/BudgetLine.cs          BudgetLine, WatchedCategory
src/MyFinance.Data/Services/BudgetService.cs       set, clear, copy across the year
src/MyFinance.App/ViewModels/Pages/BudgetPageViewModel.cs
```

## Risks

- **The rollover chain is the subtle part.** A window starting late must not truncate it.
  Covered by `The_rollover_chain_is_complete_even_when_the_window_starts_late` and by a test
  over the real book.
- **Division by zero** on a zero budget, and on a category that appeared from nothing.
  Covered explicitly rather than by inspection.
- **Only monthly periods are modelled.** `BudgetPeriodType` leaves room, but nothing else
  is implemented; a quarterly budget would need the calculator's chain revisited.
