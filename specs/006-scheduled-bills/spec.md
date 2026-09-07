# Feature Specification: Scheduled bills and the cash-flow forecast

**Folder:** `006-scheduled-bills`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "Track the bills and deposits that repeat, enter them into the register when they
fall due, and show me whether the account is going to run short before payday."

## User Scenarios & Testing

### Primary user story

Most of what leaves an account every month is known in advance. The user wants those
recurring items recorded once, listed with what is overdue at the top, entered into the
register on the day they were actually due, and projected forward so they can see whether the
balance dips below zero at any point — not just where it ends up.

### Acceptance scenarios

1. **Given** a bill due monthly on the 15th, **When** the bills summary is shown, **Then** it
   lists the amount, next due date, frequency, payment method and account, with overdue rows
   first.
2. **Given** a bill nine occurrences behind, **When** the summary is shown, **Then** it says
   how far behind it is and how many occurrences have piled up.
3. **Given** that same backlog, **When** the user enters it, **Then** nine transactions are
   written, **each on the day it was actually owed**, so the running balance stays truthful.
4. **Given** an occurrence the user did not pay, **When** they skip it, **Then** it is cleared
   without writing a transaction.
5. **Given** a series set to enter itself, **When** the book is opened, **Then** occurrences
   due within its days-ahead setting are entered, once, and the application says what it did.
6. **Given** an account with scheduled outgoings and a salary, **When** the forecast is drawn,
   **Then** it shows the balance day by day and marks the **lowest point**, not just the
   closing figure.
7. **Given** a bill deleted from the schedule, **When** the register is opened, **Then** the
   transactions it already produced are still there. The money genuinely left the account.

### Edge cases

- A bill due on the 31st in a short month → clamped to the last day, and **back to the 31st**
  the following month. Stepping forward one occurrence at a time would leave it stuck at the
  28th for ever.
- A yearly bill on 29 February in a non-leap year → falls back to the 28th.
- A due date landing on a weekend → moved by the policy, and **the shift never becomes the
  anchor for the next occurrence**, or the series drifts a week at a time.
- The last occurrence when a weekend shift pushes it past the end date → kept.
- A twice-monthly series with two identical days → degrades to monthly rather than repeating
  a date.
- A twice-monthly series started on the later day → continues from the earlier day next month.
- Moving the pattern → does not disturb what has already been paid.
- Entering the same bill twice → does not record it twice. Running auto-entry twice → does not
  double up.
- A bill with no amount → refused, unless it is explicitly an estimate.
- A bill against an account this version does not model → refused.

## Requirements

### Functional requirements

**Recurrence**

- **FR-001**: The system MUST support the frequencies the reference application offered:
  once, daily, weekly, every two weeks, twice a month, every four weeks, monthly, every two
  months, quarterly, twice a year, yearly — each with an interval multiplier.
- **FR-002**: The system MUST compute every occurrence **from the series start date and its
  position**, never from the occurrence before it.
- **FR-003**: The system MUST clamp a day-of-month that a short month does not have, and MUST
  return to the intended day in the following month.
- **FR-004**: The system MUST support a weekend shift policy, and MUST NOT let a shifted date
  become the anchor for the next occurrence.
- **FR-005**: The system MUST support ending a series by date, by occurrence count, or not at
  all, and MUST keep a last occurrence that a shift pushes past the end.
- **FR-006**: The system MUST support a twice-a-month series with two days of the month,
  alternating through the months.
- **FR-007**: The system MUST express due dates as calendar days, so no clock change can move
  one.
- **FR-008**: The system MUST describe a recurrence in the words the reference application
  used, including the interval and the end condition.

**The bills list**

- **FR-009**: The system MUST list every active bill and deposit with its amount, next due
  date, frequency, payment method and account, overdue first, then by due date.
- **FR-010**: The system MUST report how far behind an overdue row is, and how many
  occurrences are past due.
- **FR-011**: The system MUST mark an amount that is only an estimate, and carry that mark
  through to the projection.
- **FR-012**: The system MUST let a series be deactivated without losing it.

**Entering**

- **FR-013**: The system MUST write an entered occurrence into the register **on the day it
  was due**, not on today.
- **FR-014**: The system MUST enter a backlog in one action, each occurrence on its own day.
- **FR-015**: The system MUST support skipping an occurrence without writing anything.
- **FR-016**: The system MUST copy the series' category allocation onto the generated
  transaction, and MUST refuse an allocation that does not add up to the amount.
- **FR-017**: The system MUST allow a different actual amount, applying it to the single
  category, or to the first line of a split.
- **FR-018**: The system MUST record only **what was done** about each due date — entered or
  skipped — and MUST NOT store the due dates themselves. The recurrence rule is the source of
  truth about when something falls due; the stored rows are what stops a bill being paid
  twice.
- **FR-019**: The system MUST NOT enter the same occurrence twice.
- **FR-020**: The system MUST auto-enter only series explicitly set to do so, reaching forward
  by that series' own days-ahead setting, and MUST report what it did.
- **FR-021**: The system MUST leave the transactions a deleted series already produced in the
  register.
- **FR-022**: The system MUST refuse to schedule against an account type it does not model.

**Forecasting**

- **FR-023**: The system MUST project the balance forward day by day over a window, from the
  balance at the start of that window.
- **FR-024**: The system MUST net income and outgoings by day.
- **FR-025**: The system MUST find and report the **lowest point** in the projection, not only
  the closing figure. A large bill early in the month followed by a salary can end
  comfortably while going overdrawn in between.
- **FR-026**: The system MUST NOT project an occurrence that has already been entered.
- **FR-027**: The system MUST support forecasting one account or all of them together.
- **FR-028**: The system MUST show a calendar strip of several months with due dates picked
  out, and the account's balance now and after everything scheduled has gone out.

### Non-functional requirements

- **NFR-001**: Recurrence and projection MUST be pure functions, testable without a database
  or a window.
- **NFR-002**: Occurrence computation MUST be stable across time zones and clock changes.

## Key Entities

- **Scheduled transaction** — account, payee, memo, signed amount, estimate flag, payment
  method; the recurrence (frequency, interval, start date, end condition, second day of
  month, weekend shift); the behaviour (auto-enter, days ahead, active); and a category
  allocation template.
- **Occurrence history** — one row per due date *acted on*: entered (with the transaction it
  produced) or skipped.
- **Projection point** — a date, the net movement on it, and the running balance.

## Success Criteria

- **SC-001**: A bill due on the 31st is clamped in a short month and returns to the 31st
  afterwards. *`RecurrenceCalculatorTests.The_thirty_first_is_clamped_in_a_short_month`,
  `A_bill_due_on_the_thirty_first_returns_to_the_thirty_first_after_a_short_month`.*
- **SC-002**: A weekend shift never becomes the anchor for the next occurrence.
  *`A_shift_never_becomes_the_anchor_for_the_next_occurrence`,
  `A_shifted_series_still_returns_every_occurrence_in_a_window`.*
- **SC-003**: A clock change cannot move a due date.
  *`A_clock_change_cannot_move_a_due_date`.*
- **SC-004**: Nine occurrences fall past due over four months, and entering the backlog writes
  nine transactions each on its own day.
  *`RecurrenceCalculatorTests.Nine_occurrences_fall_past_due_over_four_months`,
  `ScheduleServiceTests.A_backlog_can_be_entered_in_one_action_with_each_on_its_own_day`,
  `Entering_a_bill_writes_it_into_the_register_on_the_day_it_was_due`.*
- **SC-005**: Entering the same bill twice, or running auto-entry twice, records it once.
  *`Entering_the_same_bill_twice_does_not_record_it_twice`,
  `Running_auto_entry_twice_does_not_double_up`.*
- **SC-006**: The forecast finds the point where the account would go short, including when
  the closing figure looks healthy.
  *`CashFlowForecasterTests.The_forecast_finds_the_point_where_the_account_would_go_short`,
  `The_lowest_point_is_found_even_when_the_close_looks_healthy`.*
- **SC-007**: A bill already entered is not projected again.
  *`A_bill_already_entered_is_not_projected_again`.*
- **SC-008**: Deleting a schedule leaves its transactions.
  *`ScheduleServiceTests.Deleting_a_schedule_leaves_the_transactions_it_produced`.*
- **SC-009**: Frequencies read the way the reference application words them.
  *`Frequencies_read_the_way_the_reference_books_word_them`, `An_interval_is_spelled_out`,
  `The_end_condition_is_spelled_out`.*

## Assumptions

- Depends on constitution principles **4** (recurrence and forecasting are pure, in
  `MyFinance.Core`), **2** (a bill is negative, a paycheque positive), **3** (entering goes
  through a service, so it produces ordinary transactions with all their invariants).
- A projected bill is **not** a transaction until it is entered. This is the opposite of what
  Money did, and it is why a migrated Money balance can look wildly wrong — see
  `009-money-migration`.
- Business days for the weekend shift mean Saturday and Sunday. No holiday calendar.

## Out of Scope

- A holiday calendar for the weekend shift.
- Paying a bill — this writes a register entry, it does not move money.
- Reminders, notifications or anything outside the application.
- Importing recurring bill definitions from a Money file. See `014-scheduled-bill-migration`.
