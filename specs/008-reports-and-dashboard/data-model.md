# Data Model: Reports, charts and the dashboard

This feature owns no persisted entities except `WatchedCategory` (documented under
`007-budgets`). Everything here is a **shape computed from the register**.

## What the engine consumes

**ReportEntry** — a flat snapshot per split: date, account, category (nullable), payee,
amount, and the flags that decide inclusion (transfer, void). Flat and plain, so the engine
is a pure function and can be tested in milliseconds.

## What the engine produces

### GroupedReport

Rows, a total, and what was left out.

**ReportGroup** — `Key` (a category or payee id, or **null for unassigned**), `Label`,
`Amount` (signed), `Count`, `Share` (decimal, 0–1, set by the engine).
`Magnitude` is derived — the absolute value, which is what a spending chart plots.

The null key is load-bearing: it is what lets the uncategorized row be drilled into like any
other row rather than being a caption.

### TimeSeriesReport

**ReportPeriodTotal** — period start, an axis label (`"Mar 2026"`), `Income` **positive** and
`Spending` **negative**, with `Net` and `SpendingMagnitude` derived. Keeping spending negative
in the model and taking the magnitude at the chart is what stops a sign convention leaking
into the arithmetic.

### AccountBalanceRow

Account id, name, group, balance — grouped and signed exactly as the account list shows them,
so two screens cannot disagree about the same account.

### NetWorthPoint

Date, label, `Assets`, `Liabilities`. Net worth counts **every movement up to the date**, not
only those inside the reporting window — a balance is cumulative, not a period figure.

### ComparisonRow / ComparisonReport

Key, label, `First`, `Second`. A category that appeared from nothing has **no** percentage
change: there is no denominator, and inventing one (or showing ∞) is worse than a blank.

## Chart geometry

Computed from the shapes above, never from the database:

- **Bar length** — scaled against the longest bar, not the total.
- **Axis top** — rounded up to a figure a person would choose.
- **Scale** — always includes zero; extends below it when the data goes negative.
- **Ticks** — evenly spaced from the baseline.
- **Points** — left to right across the viewport, higher values higher on screen, single point
  centred, culture-independent point strings.
- **Key per bar** — so a click can drill into it.

## Export

Any report, at any time, as CSV. Constitution principle 10: Money's file format is the reason
this application exists, and nothing here should be readable only from inside it. *(The
report-level export is the part of principle 10 that is kept today; the book-level export is
`012-full-book-export`.)*
