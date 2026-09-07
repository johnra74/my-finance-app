# Data Model: Investment accounts

One new **primitive**, four new entities, one changed enum. The primitive is where the
difficulty is.

## Quantity (new primitive)

`Money` is a `readonly record struct` over a `long` count of **cents**. A holding is 12.3456
shares — a different scale, sometimes eight decimal places, and `double` is ruled out by
constitution 1 for the reasons `MoneyTests.Repeated_addition_stays_exact_where_double_would_drift`
demonstrates.

`Quantity` mirrors `Money`'s shape deliberately, so it is familiar and so its guarantees are
the same ones:

| Property | Notes |
|---|---|
| Scaled integer | A `long` of scaled units, with a fixed scale sufficient for fractional shares. |
| Exact arithmetic | Addition and subtraction lose nothing. |
| Rounding | Half away from zero, matching `Money`. |
| Overflow | Detected, not wrapped. |
| Zero | `Quantity.Zero`, so summing an empty sequence works. |

It is built and tested **first**, because every figure in this feature derives from it.

All four entities live in `src/MyFinance.Core/Entities/Investments.cs` — they are one small
graph and splitting them across four files would only make the relationships harder to read.

## Security

`Id`, `Name`, `Symbol`, `Type` (share, fund, bond, other).

Identity is the security, not the holding: the same fund held in two accounts is one
`Security` and two `Holding` rows, so a price entered once values both.

## Holding

| Field | Notes |
|---|---|
| `Id`, `AccountId`, `SecurityId` | |
| `Quantity` | Exact, via the type above. |
| `CostBasis` | `Money`. **Average cost for the whole holding**, not per lot. |

A sell removes quantity and a **proportional** share of cost. A holding sold to zero is kept
at zero rather than deleted, so its history survives — the same reasoning that keeps closed
accounts and archived categories (`003-categories-and-payees`).

**No lots.** Enough for "what do I hold" and "what did I pay"; not enough for capital gains,
which need a lot-matching policy the user would have to answer per sale. This application has
no tax report — see the gap table — so lots would serve a feature that does not exist. A tax
report, if ever specified, brings them with it.

## InvestmentTransaction

`Id`, `AccountId`, `SecurityId`, `Date`, `Kind` (buy, sell, dividend, reinvestment, fee),
`Quantity`, price per unit, total `Money`, fees, and **the cash-leg transaction it produced**.

That last link is what keeps this from becoming a second ledger: the cash movement is an
ordinary `Transaction` written through `RegisterService`, inheriting sequencing, the running
balance and the split invariant. Only the holding side is new.

Structurally identifiable, which is what lets `ReportEngine` exclude investment activity from
spending and income with a predicate — the same way it already excludes transfers, and not by
heuristic.

## SecurityPrice

`Id`, `SecurityId`, **`AsOf` (`DateOnly`)**, `Price` (`Money`).

`AsOf` is not metadata. Prices are hand-entered (constitution 7 rules out fetching them), so a
value is only as current as the last time somebody typed one. **Every valued figure is shown
with the date of the price behind it** — constitution 5 applied to a number: a figure whose
basis the user cannot see is one they must check from scratch.

A security with no price at all is valued **at cost**, and says so.

## AccountType (changed)

`Brokerage` is added, and brokerage accounts stop arriving as
`UnsupportedImported = 99`.

**Loans and mortgages stay in that bucket.** They are their own gap in the gap table, and
folding them in here would double the feature.

## Derived, never stored

- **Holding value** — quantity × the most recent price at or before a date, with that date
  carried alongside; cost when no price exists.
- **Account value** — cash balance plus the value of its holdings.
- **Net worth** — as today, plus holdings. Note `002-accounts-and-register`'s rule still
  applies: nothing here is stored, because a stored valuation would be a second source of
  truth that goes stale the moment a price is entered.
- **Average cost per unit** — cost basis ÷ quantity, computed for display only.
