# Data Model: Budgets

## BudgetLine

| Field | Notes |
|---|---|
| `Id`, `CategoryId` | |
| `PeriodStart` | **First day** of the budgeted period. A figure set from any day in the month is stored against the first. |
| `PeriodType` | `BudgetPeriodType`. Monthly is what is implemented; the year view is twelve monthly lines rolled together, not a separate row. |
| `Amount` | `Money`, **always positive**, whatever the caller supplied and whatever kind the category is. The sign convention is applied once, at comparison time. |
| `RollsOver` | Carry an unspent remainder into the next period. Per category, not global. |
| `Notes` | |

**Uniqueness:** one line per (category, period start). Setting a budget twice replaces it.

## WatchedCategory

A category surfaced on the Home dashboard's tracker tile. Reports how it is tracking even
when it has no budget line at all — "what have I spent on this" is a useful question on its
own. Watching an already-watched category stops watching it.

## Derived, never stored

- **Spent** — from the report engine, over the same window, with the same rules about
  transfers, voids and splits. Never a second query.
- **Remaining** — budget (plus any carried surplus) minus spent. **Negative when overspent**;
  not clamped.
- **Carried surplus** — the rollover chain, computed from where the rollover began rather
  than from the start of the displayed window. **Surplus only**: an overspend is not carried
  forward as a debt.
- **Fraction used** — for the meter. A zero budget spent against reads as fully used rather
  than dividing by zero.
- **The over-budget count** — spending categories only.
- **The average left over.**
