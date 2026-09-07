# Implementation Plan: More than one currency

**Spec:** `./spec.md` · **Status:** **Partly built** — the type and the totals; persistence deferred

## Summary

`Money` gains a currency and refuses arithmetic across two of them; totals that span
currencies become per-currency subtotals; **no rates exist anywhere.** The decision this turns
on: **refuse rather than convert.** Every cost in this feature is the cost of touching the
type the whole application is built on, and refusing conversion is what keeps that cost to the
type itself instead of spreading a rate policy through reports, budgets and net worth.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 1 — integer cents | **This is the principle at risk.** `MinorUnitsPerUnit` is a `const 100`, which is false for JPY (0 decimals) and for three-decimal currencies. The exponent has to come from the currency, and it must stay integer arithmetic throughout. |
| 7 — nothing leaves the machine | No rate source, because there are no rates. |
| 2 — signs from the owning account | Unchanged, and it is what makes per-currency subtotals meaningful. |
| 4 — correctness outside WPF | The whole change lands in `MyFinance.Core`, where it is tested. |

## Technical context

- **Projects:** `MyFinance.Core/Primitives` (the type), `MyFinance.Core/{Registers,Accounts,Reporting,Budgeting}`
  (every total), `MyFinance.Data` (persistence and a migration), `MyFinance.App` (display).
- **The cost is in the existing 836 tests and every arithmetic path, not in new code.** That
  is the organising fact of this plan.
- **Testing:** `tests/MyFinance.Core.Tests/Primitives/MoneyTests.cs` is the centre of gravity;
  every suite is affected.

## Design

### Where the cost actually is

```csharp
// src/MyFinance.Core/Primitives/Money.cs, today
public const int MinorUnitsPerUnit = 100;
public static Money operator +(Money l, Money r) => new(checked(l.MinorUnits + r.MinorUnits));
```

Two problems, and neither is additive:

1. `100` is a compile-time constant that is **wrong for JPY and for three-decimal
   currencies**. It appears in `FromDecimal`, `ToDecimal`, `FromUnits`, formatting and
   parsing.
2. `operator +` has no idea whether its operands are comparable. Today that is correct because
   they always are.

### Staged so the suite is green after every task

`SC-004` — a single-currency book behaves exactly as it does today — is not a final
acceptance criterion. It is the **invariant held after every task**, which is what makes a
change of this size reviewable at all:

1. Introduce `Currency` (code, minor-unit exponent, formatting) with a `Currency.Default`.
   Nothing consumes it yet.
2. Give `Money` a currency defaulting to `Currency.Default`, and derive the scale from it.
   Every existing call site keeps working, because every existing amount is in one currency.
3. Make mixed-currency arithmetic fail loudly. Still no behaviour change: nothing mixes yet.
4. Change the aggregation points — account list, reports, budgets, net worth — to group by
   currency, producing a single group for a single-currency book.
5. Only then allow an account in another currency to be created.

Step 5 last is the whole discipline. Allowing a second currency before the totals can express
one is how a book gets a number nobody can defend.

### Refuse loudly, not silently

`money1 + money2` across currencies **throws**. It does not return zero, coerce, or pick the
left operand. This is a programming error, not a user error — the user should never be able to
reach it, because the aggregation points group first. A silent result here is the
`Account.CurrencyCode`-shaped bug this spec exists to prevent, one layer deeper.

### Totals become per-currency groups

`AccountListBuilder`, `ReportEngine`, `BudgetCalculator` and net worth all produce
**per-currency** results. For a single-currency book that is one group, rendered exactly as
today — no visible change, no extra heading, no "USD" label on a book that has only ever seen
dollars.

### Cross-currency transfers break an invariant, deliberately

`002-accounts-and-register` FR-026 says a transfer's legs cancel. Across currencies they
cannot: $100 leaves and £78 arrives, as the two statements state them. `TransactionValidator`
must therefore check cancellation **only within a currency**, and check pairing structurally
across currencies.

This is called out rather than absorbed because
`TransactionValidatorTests.Transfer_pairs_always_net_to_zero_across_the_books` is one of the
strongest assertions in the codebase, and weakening it must be a deliberate, visible act.

### Alternatives rejected

- **A book currency with hand-entered rates.** Gives the single net-worth figure people want,
  and brings a rate table, a staleness problem, a historical-versus-current policy for every
  report, and a number that is quietly wrong whenever the rate is old. Declined by the user.
- **Leaving `Money` alone and carrying currency on the account.** Exactly today's design, and
  exactly the bug: the arithmetic cannot see it.
- **A separate `ForeignMoney` type.** Two money types, and every function needing both.
- **Keeping `MinorUnitsPerUnit = 100` and treating JPY as cents.** Every JPY amount off by a
  factor of a hundred, forever.
- **Doing this only when a second currency is added.** The field already exists and already
  promises something the arithmetic does not deliver.

## Project structure

```
src/MyFinance.Core/Primitives/Currency.cs      NEW — code, exponent, formatting
src/MyFinance.Core/Primitives/Money.cs         + currency; scale from it; mixed ops throw
src/MyFinance.Core/Registers/BalanceCalculator.cs   per-currency
src/MyFinance.Core/Accounts/AccountListBuilder.cs   per-currency subtotals, no grand total
src/MyFinance.Core/Reporting/ReportEngine.cs        per-currency
src/MyFinance.Core/Budgeting/BudgetCalculator.cs    per-currency
src/MyFinance.Core/Validation/TransactionValidator.cs  cancellation within a currency
src/MyFinance.Data/Configurations/*.cs              persist the currency beside minor units
src/MyFinance.Data/Migrations/                      NEW migration, defaulting existing rows
src/MyFinance.App/                                  labels, and the currency picker
```

## Risks

- **This touches the type every feature depends on.** 836 tests are the safety net and also
  the bill. Contained by the staging above, so the suite is green after each task rather than
  only at the end.
- **The weakened transfer invariant is the sharpest edge.** Contained by making the change
  explicit in `TransactionValidator` and by keeping the same-currency assertion exactly as
  strong as it is today.
- **A user may still want one net-worth figure**, and the answer is "we do not convert". That
  is a real limitation, honestly chosen, and the spec says so rather than hiding it.
- **Rounding at the boundary.** With no conversion there is none — which is the quiet second
  benefit of this decision, and the reason `NFR-001` is satisfiable at all.

## What changed during implementation

1. **Zero had to become currency-agnostic.** `Money.Zero` is `default(Money)`, which a great
   deal of code relies on, and a sum has to start somewhere. So zero is compatible with every
   currency and takes the currency of whatever it is added to. Without that, `Money.Sum` on a
   sequence of pounds would have thrown on its first step.
2. **Equality had to be written by hand.** A `record struct` compares every field, so an
   unstamped zero would not have equalled an explicitly-dollar zero — and `default(Money)` is
   everywhere. Equality now compares the count, and the currency only when the count is
   non-zero. Ordering still refuses across currencies; equality does not need to, because
   "these are not the same amount" is answerable without a rate.
3. **The transfer check needed a third rule, not just a weakened one.** Dropping cancellation
   across currencies would have left a cross-currency pair with *no* arithmetic check at all.
   One thing is still checkable without a rate: money left one account, so it must have
   arrived in the other. Two legs pointing the same way creates value at any rate, and is
   refused.
4. **`Money.cs`'s own doc comment argued against this change.** It said currency "is
   deliberately not carried here… If multi-currency arrives, it belongs in a separate wrapper
   type." The spec considered that and rejected it — two money types, and every function
   needing both — so the comment was rewritten rather than left contradicting the code beneath
   it.

## Why persistence stopped

See the closing section of `spec.md`. In short: EF's value-converter path cannot carry two
columns into one struct, so the "small sibling column" the plan assumed is really an
interceptor or a query-breaking rewrite. That is a schema commitment with two defensible
shapes and an expensive reversal, and it buys nothing until an account in another currency can
exist — which `T012`, deliberately last, does not yet allow.

