# Data Model: Scheduled bills and the cash-flow forecast

## ScheduledTransaction

| Field | Notes |
|---|---|
| `Id`, `AccountId`, `PayeeId?`, `Memo` | |
| `Amount` | `Money`, **signed like a transaction**: a bill is negative, a paycheque positive. |
| `IsEstimate` | The amount varies; this is a forecast. The reference application marks these with a trailing `~`. Carried through to the projection so it can say so. |
| `PaymentMethod` | Shown on the bills summary. |

### Recurrence

| Field | Notes |
|---|---|
| `Frequency` | Once, Daily, Weekly, EveryTwoWeeks, TwiceAMonth, EveryFourWeeks, Monthly, EveryTwoMonths, Quarterly, TwiceAYear, Yearly. |
| `Interval` | Multiplier on the frequency. 2 with Monthly is every two months. |
| `StartDate` | `DateOnly`. **The anchor every later date is computed from.** |
| `EndKind`, `EndDate?`, `OccurrenceCount?` | Never, by date, or after *n*. |
| `SecondDayOfMonth?` | For TwiceAMonth. Two equal days degrade to monthly rather than repeating a date. |
| `WeekendShift` | None, PreviousBusinessDay, NextBusinessDay. Applied to the computed date and **never fed back as the anchor**. |

### Behaviour

`AutoEnter`, `DaysAheadToEnter` (default 5), `IsActive`.

### Splits

A category allocation template, copied onto each generated transaction. Must sum to the
amount, exactly as an ordinary transaction's splits must (`002-accounts-and-register`).

## Occurrence history

One row per due date **acted on**:

| Field | Notes |
|---|---|
| The series | |
| The due date | The one that was acted on. |
| What happened | Entered or skipped. |
| The transaction produced | For an entry. |

**Due dates themselves are not stored.** The recurrence rule is the source of truth about
when something falls due; these rows say which of those were dealt with, and exist to stop a
bill being paid twice. This is why moving the pattern does not disturb what has already been
paid.

## Derived, never stored

- **The next due date** — the first occurrence on or after today that has not been acted on.
- **How overdue, and how many occurrences** — counted from the rule.
- **The projection** — a sequence of (date, net movement, running balance), from the balance
  at the start of the window forward, excluding occurrences already entered.
- **The low point** — the minimum of that sequence. The figure the forecast exists to report.
