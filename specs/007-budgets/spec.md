# Feature Specification: Budgets

**Folder:** `007-budgets`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "A monthly figure per category, and what actually happened against it — with
unspent money able to carry forward."

## User Scenarios & Testing

### Primary user story

The user sets a monthly figure for the categories they want to control, and wants to see, at
a glance, what they planned against what they spent, with the numbers written beside the
meter rather than only shown as a bar. Some categories — a clothing allowance, a car repair
fund — only make sense if what goes unspent this month is available next month.

### Acceptance scenarios

1. **Given** a category and a month, **When** a figure is set, **Then** the budget for that
   month is that figure, however the amount was signed when it was supplied.
2. **Given** a budget of $400 and $250 spent, **When** the budget page is shown, **Then** it
   reports $150 remaining.
3. **Given** a budget of $400 and $460 spent, **When** it is shown, **Then** it reports a
   negative remainder — going over is reported, not hidden.
4. **Given** a category with rollover on and $150 unspent, **When** the next month is shown,
   **Then** the $150 is available on top of that month's figure.
5. **Given** a category with rollover on that **overspent**, **When** the next month is shown,
   **Then** next month starts from the figure that was set for it. An overspend is never
   carried forward as a debt.
6. **Given** a figure set for January, **When** the user copies it across the year, **Then**
   the remaining months take it, leaving months that already have a figure alone.
7. **Given** the whole year, **When** the year view is shown, **Then** twelve months roll into
   one line per category.

### Edge cases

- A book with no budget at all → produces nothing, rather than throwing.
- A month with nothing in it → still a month, shown as one.
- A budget of zero that has been spent against → reads as fully used, without dividing by
  zero.
- Setting a budget twice for the same category and month → replaces it, rather than adding a
  second line.
- A budget set from any day of the month → applies to that month.
- Budgeting a category that has been deleted → refused.
- A rollover chain whose window starts partway through → the chain is still complete, computed
  from where the rollover actually began.

## Requirements

### Functional requirements

- **FR-001**: The system MUST hold one budgeted amount per category per period.
- **FR-002**: The system MUST store the amount positive regardless of how it was supplied or
  what kind the category is.
- **FR-003**: The system MUST accept a budget set from any day within the period it applies
  to.
- **FR-004**: The system MUST replace an existing figure rather than accumulating a second.
- **FR-005**: The system MUST allow a budget to be cleared.
- **FR-006**: The system MUST refuse to budget a category that no longer exists.
- **FR-007**: The system MUST measure a budget against what was **actually spent**, using the
  same figures the spending report produces for the same window.
- **FR-008**: The system MUST report what is left, and MUST report an overspend as a negative
  remainder rather than clamping it to zero.
- **FR-009**: The system MUST support carrying an unspent remainder into the next period, per
  category.
- **FR-010**: The system MUST NOT carry an overspend forward. Next period starts from the
  figure set for it.
- **FR-011**: The system MUST compute a complete rollover chain even when the displayed window
  starts after the rollover began.
- **FR-012**: The system MUST support copying one month's figures across the year, leaving
  months that already have a figure alone unless told otherwise.
- **FR-013**: The system MUST offer a whole-year view rolling twelve months into one line per
  category.
- **FR-014**: The system MUST return lines in category order.
- **FR-015**: The system MUST count only spending categories when reporting how many are over
  budget.
- **FR-016**: The system MUST support watching a category, reporting how it is tracking on the
  dashboard, including when it has no budget at all; watching a category twice MUST stop
  watching it.
- **FR-017**: The system MUST report an average of what is typically left over.

### Non-functional requirements

- **NFR-001**: Budget arithmetic MUST be a pure function, testable without a database.
- **NFR-002**: A budget figure and the corresponding spending report MUST agree to the cent
  for the same month. Two numbers for the same fact is worse than one.

## Key Entities

- **Budget line** — a category, the first day of the period, the period type, the amount
  (always positive), a rollover flag, and notes.
- **Watched category** — a category surfaced on the dashboard's tracker tile.

## Success Criteria

- **SC-001**: A budget and a spending report agree about the same month.
  *`ReportServiceTests.A_budget_and_a_spending_report_agree_about_the_same_month`.*
- **SC-002**: An unspent remainder carries forward when asked; an overspend does not become a
  debt. *`BudgetCalculatorTests.An_unspent_remainder_carries_forward_when_asked`,
  `An_overspend_does_not_become_a_debt_against_the_next_month`,
  `Without_rollover_each_period_starts_afresh`.*
- **SC-003**: The rollover chain is complete even when the window starts late.
  *`The_rollover_chain_is_complete_even_when_the_window_starts_late`; and against the real
  book, `Rollover_carries_through_the_real_book`.*
- **SC-004**: A zero budget spent against reads as fully used, without dividing by zero.
  *`A_zero_budget_that_has_been_spent_against_reads_as_fully_used`.*
- **SC-005**: Setting a budget twice replaces it.
  *`Setting_a_budget_twice_replaces_it_rather_than_adding_a_second`.*
- **SC-006**: Copying across the year leaves existing figures alone.
  *`A_month_can_be_copied_across_a_year`, `Copying_leaves_an_existing_figure_alone_unless_told_otherwise`.*
- **SC-007**: A book with no budget produces an empty result rather than an error.
  *`A_book_with_no_budget_produces_nothing_rather_than_throwing`.*

## Assumptions

- Depends on constitution principles **4** (`BudgetCalculator` is pure, in `MyFinance.Core`),
  **1** (integer cents), **3** (writes through `BudgetService`).
- A budget is monthly, with a year view over the twelve months. Weekly or fortnightly budgets
  are not offered.
- Rollover is a per-category choice, not a global mode.
- Spending figures come from the same engine as the reports, so the two can never disagree.

## Out of Scope

- Budgeting income, or a whole-book "money left over" plan.
- Envelope budgeting, or moving money between categories mid-month.
- Forecasting a budget from history rather than the user setting one.
- Alerts or notifications when a budget is exceeded.
