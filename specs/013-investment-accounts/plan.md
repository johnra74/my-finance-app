# Implementation Plan: Investment accounts

**Spec:** `./spec.md` · **Status:** **Built** — see `./tasks.md`

## Summary

Four entities — Security, Holding, InvestmentTransaction, Price — plus a **new exact quantity
type**, because `Money` is integer cents and cannot express a share count. Holdings count
toward net worth at a hand-entered price, always shown with the date that price applies to.
The decision this turns on: **bookkeeping, not portfolio management.** Average cost, no lots,
no corporate actions, no market data — the smallest feature that makes net worth true.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 1 — exactness | `Money` cannot hold 12.3456 shares, and `double` is not an option. A new exact `Quantity` type over scaled integers is the whole reason this feature is large. |
| 7 — nothing leaves the machine | No price feed. This is what forces hand-entered prices, and the constraint is accepted rather than worked around. |
| 2 — signs from the owning account | A buy's cash leg is an ordinary negative transaction; the register keeps working as it does. |
| 5 — a guess is never applied unseen | A value derived from a year-old price **is** a guess. It is shown with its date, and a holding with no price is valued at cost and says so. |
| 3 — writes through a service | A new `InvestmentService`, alongside `RegisterService`. |

## Technical context

- **Projects:** `MyFinance.Core/{Primitives,Entities,Investments}`, `MyFinance.Data`
  (entities, configurations, a migration, a service), `MyFinance.App` (a register variant).
- **Existing pieces reused:** `Money` for cash and cost; `RegisterService`'s write path for
  cash legs; `AccountType` (replacing the balance-only `UnsupportedImported` for brokerage
  accounts); `ReportEngine`'s net-worth path; `MoneyReader`, which already reaches the
  holdings tables in a `.mny`.
- **Testing:** `MyFinance.Core.Tests` for the quantity type and the average-cost arithmetic —
  where the correctness actually lives; `MyFinance.Data.Tests` for the service and migration.

## Design

### The quantity type is the real work

`Money` is a `long` count of cents. A holding is 12.3456 shares — a different scale, and
sometimes eight decimal places. `double` is out under constitution 1, for exactly the reasons
`MoneyTests.Repeated_addition_stays_exact_where_double_would_drift` demonstrates.

So: `Quantity`, a `readonly record struct` over a scaled integer, mirroring `Money`'s shape —
exact addition, half-away-from-zero rounding, overflow detected rather than wrapped. It is
built and tested **before anything else**, because every figure in the feature is derived from
it.

### Average cost, and what that costs

One cost figure per holding. A buy adds quantity and cost; a sell removes quantity and a
**proportional** share of cost. Simple, exact, and enough to answer "what do I hold" and "what
did I pay".

It is **not** enough for capital gains, which need lots and a matching policy (FIFO? specific
lot?) that the user would have to answer per sale. This application has no tax report — the
gap table records tax-line assignment as its own unfilled gap — so lots would be machinery in
service of a feature that does not exist. If a tax report is ever specified, it brings lots
with it and this decision is revisited there.

### Prices are hand-entered, and their age is part of the figure

Constitution 7 rules out fetching prices. So a value is only as current as the last time
someone typed one — and the honest response is to **show the date beside the figure**, not to
present a year-old valuation as though it were today's.

This is constitution 5 applied to a number rather than a category: a figure whose basis the
user cannot see is one they have to check from scratch. A holding with no price is valued at
cost and says so.

### Investment activity is excluded the way transfers are

Buying shares is not spending; a dividend into cash is arguably income and is filed as such
only if the user categorises it. `ReportEngine` already excludes transfers with a single
predicate because they are structurally identifiable — investment transactions get the same
treatment, not a heuristic.

### Cash legs stay ordinary transactions

A buy writes a normal negative transaction into the account's cash register through
`RegisterService`. It therefore inherits every invariant: splits summing, sequencing within a
day, the running balance. Only the *holding* side is new. This is what keeps the feature from
becoming a second ledger with its own rules.

### Replacing the balance-only stopgap

`AccountType.UnsupportedImported = 99` currently absorbs brokerage, loan and mortgage
accounts. This feature takes brokerage out of that bucket. **Loans and mortgages stay in it** —
they are a separate gap, listed in the gap table, and conflating them here would double the
feature.

### Alternatives rejected

- **Individual lots.** See above; revisit with a tax report.
- **`double` for quantities.** Constitution 1.
- **Reusing `Money` for quantities.** Two decimal places is wrong for shares, and a type
  meaning both money and quantity means neither.
- **A price feed, or an import path for prices.** Constitution 7 forbids the first; the second
  is a whole import feature for a secondary capability, and can be added later without
  changing the model.
- **Carrying holdings at cost only.** Never misleading, and it does not answer the question
  net worth is asked for.
- **A separate investment register with its own rules.** Cash legs would then need their own
  balance logic, and the two would drift.

## Project structure

```
src/MyFinance.Core/Primitives/Quantity.cs           NEW — exact, scaled integer
src/MyFinance.Core/Entities/Investments.cs          NEW — Security, Holding,
                                                    InvestmentTransaction, SecurityPrice
src/MyFinance.Core/Investments/HoldingCalculator.cs NEW — average cost, valuation, staleness
src/MyFinance.Core/Reporting/ReportEngine.cs        net worth includes holdings; activity excluded
src/MyFinance.Core/Enums/Enums.cs                   + AccountType.Brokerage
src/MyFinance.Data/Services/InvestmentService.cs    NEW
src/MyFinance.Data/Migrations/                      NEW migration
src/MyFinance.Import/Mny/MoneyReader.cs             read holdings (tables already reachable)
src/MyFinance.Data/Services/MigrationService.cs     + a holdings stage
src/MyFinance.App/                                  a holdings view and its editor
tests/MyFinance.Core.Tests/Primitives/QuantityTests.cs        NEW
tests/MyFinance.Core.Tests/Investments/HoldingCalculatorTests.cs NEW
tests/MyFinance.Data.Tests/Services/InvestmentServiceTests.cs    NEW
```

## Risks

- **This is the largest gap and the least likely to be built soon.** The task list is
  deliberately kept to the level the average-cost decision supports; a forty-task plan would be
  the least accurate document in the repository.
- **Collision with `015-multi-currency`.** Both add to `MyFinance.Core/Primitives` and both
  touch net worth. The build order puts `015` first; whichever runs second inherits a merge.
- **Stale prices are the honest weakness**, and they are structural, not a bug. Contained by
  showing the date beside every valued figure rather than by pretending otherwise.
- **Corporate actions will make some holdings wrong.** A real limitation of a first version,
  stated in FR-009 rather than discovered by a user whose share count halved and whose
  application did not notice.

## What changed during implementation

1. **Net worth needed a replay, not just a valuation.** The plan valued holdings at each point
   of the net-worth chart; that would have valued *today's* holdings at every historical date,
   showing shares as owned years before they were bought — and the error grows the further back
   you look, which is exactly where somebody goes to see how they have done.
   `HoldingCalculator.StateAt` replays purchases and sales to each date instead.
2. **`Security` collides with the `MyFinance.Data.Security` namespace.** Aliased in the data
   layer rather than renaming a correct domain word.
3. **Money records no cost basis.** See the closing section of `spec.md`. The migration brings
   quantity and prices and reports the gap.
4. **The schema-version interlock earned its keep.** Adding the migration failed
   `SchemaUpgradeTests.The_current_schema_version_matches_the_migration_count` immediately —
   exactly what `017` built it for. `BookSchema.Current` is now 4.
5. **One `017` test had to be reframed.** `A_book_with_no_recorded_version_is_treated_as_current`
   assumed `Unstamped == Current`, which stopped being true the moment a fourth migration
   shipped. A pre-versioning book is now correctly offered an upgrade rather than opened as
   current; the durable invariant — *never refuse an old book* — is asserted instead, and a
   second test covers the upgrade itself.

