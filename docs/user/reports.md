# Reports and the dashboard

Under the **Reports** tab.

## The eight reports

| Report | |
|---|---|
| Spending by category | Where it went |
| Spending by payee | Who got it |
| Income by category | Where it came from |
| Income and spending over time | Both, on one scale |
| This period against the last | What changed |
| Account balances | A snapshot |
| Net worth over time | Including investment holdings, valued from the prices held at each point |
| Every transaction | The detail behind everything else |

Each takes a date range and, optionally, a single account. Subcategories can be rolled up
under their heading.

## A chart and a table, always both

The chart shows the shape; the table gives the figures. **Neither on its own is the report.**
Having both is also what makes a report readable without relying on colour.

## Drill-down

Double-click any row to list the transactions behind it, and double-click one of those to
land on it in the register.

The point of a report is usually the question it raises, and a figure you cannot get behind
is a dead end.

## When the figures cannot be trusted

Percentages computed over a pile of uncategorized spending are quietly wrong. So a report
says how much is unassigned and offers to list it, rather than letting you draw a conclusion
from a number that does not mean what it appears to.

## About the charts

The charts are drawn directly rather than by a charting library, and the geometry — bar
lengths, scales, gridline positions — is computed where it is covered by tests. **A bar whose
length disagrees with its label is a lie the eye cannot catch**, so that arithmetic is not
left to the layer that can only be verified by looking at it.

Two deliberate departures from what Money did:

- **Bars, not a pie, for spending by category.** These category names are long — *Bills :
  Water and Sewer* — and a pie pushes them into a legend the reader has to match back to
  slices by colour. A bar keeps the name against its length.
- **One scale per chart.** Income and spending share an axis rather than getting one each. A
  second y-axis invents a relationship the figures do not have.

The two-series palette was checked rather than eyeballed: worst adjacent separation ΔE 24.7
under protanopia and 33.6 for normal vision, both above 3:1 contrast on white. Status colours
— a budget running hot, an overdue bill — are never used as a series colour and never carry
meaning on their own; the same fact is always written out beside them.

## The dashboard

The home page shows net worth, favourite accounts, what is overdue and what falls due in the
next month, where the money went over the last thirty days, and any category you have asked
to keep an eye on.

## Getting the figures out

Any report exports to **CSV** at any time, and prints. See {doc}`printing` and
{doc}`exporting`.
