# Implementation Plan: Reports, charts and the dashboard

**Spec:** `./spec.md` · **Status:** As-built

## Summary

`ReportEngine` aggregates plain snapshots into report shapes; `ChartGeometry` turns those
shapes into coordinates. Both are pure, in `MyFinance.Core`, and both are tested. The WPF
layer draws primitives from numbers it did not compute. The decision this turns on: **charts
are drawn by hand rather than by a library**, precisely so the arithmetic is ours and can be
covered by tests.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 4 — correctness outside WPF | A bar whose length disagrees with its label is a lie the eye cannot catch, so bar lengths, scales and gridlines are computed in Core. |
| 1 — integer cents | Totals are `Money`; only shares are decimal, and they are asserted to sum to one. |
| 10 — the user's data stays theirs | Every report exports to CSV. |
| 9 — off the interface thread | Report runs go through `RunBusyAsync`. |

## Technical context

- **Projects:** `MyFinance.Core/Reporting`, `MyFinance.Data/Services/ReportService.cs`,
  `MyFinance.App/ViewModels/Pages/{Reports,Home}PageViewModel.cs`.
- **Charts:** hand-drawn WPF primitives bound to geometry computed in Core. No charting
  library.
- **Testing:** `ReportEngineTests` and `ChartGeometryTests` in Core;
  `ReportServiceTests` against the real migrated book, which is where the interesting cases
  (credit-card payments, splits, transfers, a real long tail) actually occur.

## Design

### One uniform table to aggregate over

Every transaction has at least one split, even an uncategorized one
(`002-accounts-and-register`). So every report is an aggregation over splits, with no special
case for "transactions with no category" — that is just a split with a null category, which is
also why the uncategorized row can be drilled into like any other.

### Transfers are excluded by predicate, not by heuristic

Because a transfer is two linked rows (constitution 2), excluding them is one condition. A
credit-card payment is a transfer and therefore never appears as spending — the thing every
naive spending report gets wrong, and the reason it is asserted against the real book.

### Uncategorized spending is surfaced, not absorbed

A percentage computed over a window where 30% is unassigned is not wrong in arithmetic and is
wrong in meaning. The report states the unassigned figure above the chart and offers to list
it. The alternative — folding it into an "Other" slice — lets a reader draw a conclusion the
data does not support.

### Folding, not truncating

A long tail becomes one folded row, so the parts still sum to the total. Truncating to the
top ten produces a chart whose bars do not add up to the number printed underneath.

### Chart geometry

`ChartGeometry` produces bar lengths scaled against the **longest** (not the total), axis tops
rounded to a figure a person would choose, scales that always include zero and extend below it
when the data does, points running left to right, and higher values higher on screen. Polyline
strings are culture-independent — a comma decimal separator would produce a malformed
attribute in half of Europe.

Every bar carries its key, which is what makes drill-down possible from a click.

### Two deliberate departures from the reference application

- **Bars, not a pie, for spending by category.** These names are long, and a pie pushes them
  into a legend the reader matches back to slices by colour. A bar keeps the name against its
  length, and two close categories stay tellable apart.
- **One scale per chart.** Income and spending share an axis. A second y-axis invents a
  relationship the figures do not have.

### Colour

The two-series palette was validated by measurement — worst adjacent separation ΔE 24.7 under
protanopia, 33.6 for normal vision, both above 3:1 contrast on white. Status colours (a budget
running hot, an overdue bill) are never used as a series colour and never carry meaning alone:
the same fact is always written out beside them. And every report has a table, which is the
real accessibility answer.

### Alternatives rejected

- **A charting library.** Would put the arithmetic somewhere untestable and pull a large
  dependency into a single-file build.
- **A pie chart.** See above.
- **Dual axes.** See above.
- **Truncating the long tail.** See above.
- **Computing shares in `double`.** Shares are asserted to sum to one; floating point makes
  that a tolerance test instead of an equality.

## Project structure

```
src/MyFinance.Core/Reporting/ReportEngine.cs     aggregation, folding, comparison, net worth
src/MyFinance.Core/Reporting/ReportModels.cs     GroupedReport, TimeSeriesReport, rows
src/MyFinance.Core/Reporting/ReportEntry.cs      the snapshot the engine consumes
src/MyFinance.Core/Reporting/ChartGeometry.cs    bars, scales, ticks, polylines
src/MyFinance.Data/Services/ReportService.cs     queries, drill-down, CSV export
src/MyFinance.App/ViewModels/Pages/ReportsPageViewModel.cs
src/MyFinance.App/ViewModels/Pages/HomePageViewModel.cs
```

## Risks

- **A report that silently excludes something** is the failure mode here — a filter that also
  drops rows it should not. Contained by testing against the real migrated book rather than
  constructed data, where the awkward cases genuinely exist.
- **Net worth over a window** must count every movement up to the date, not only those inside
  it. Easy to get wrong, explicitly tested.
- **Performance** on ~20,000 transactions with splits. Acceptable today; the engine consumes
  snapshots, so if it ever needs to change, the aggregation can move into SQL without the
  chart layer knowing.
