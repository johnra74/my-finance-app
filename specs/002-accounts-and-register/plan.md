# Implementation Plan: Accounts and the register

**Spec:** `./spec.md` · **Status:** As-built

## Summary

Balance arithmetic, ordering and account-list aggregation are pure functions in
`MyFinance.Core`, over plain snapshots. `MyFinance.Data/Services` owns every write and the
invariants the schema cannot express. The WPF layer holds no arithmetic at all. The decision
this turns on: **running balances are computed, never stored** — so there is exactly one
source of truth about what an account holds.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 1 — integer cents | Every amount is `Money`; `MoneyTests` asserts exactness where `double` would drift. |
| 2 — sign from the owning account | Amounts are signed at write time; a transfer is two rows validated to cancel. |
| 3 — writes through a service | `AccountService`, `RegisterService`, `ReconcileService`. The register UI never touches a context. |
| 4 — correctness outside WPF | `BalanceCalculator`, `AccountListBuilder`, `TransactionValidator` are all in `MyFinance.Core`. |
| 9 — long work off the UI thread | Register loads go through `BusyViewModel.RunBusyAsync`. |

## Technical context

- **Projects:** `MyFinance.Core/{Registers,Accounts,Validation,Primitives}`,
  `MyFinance.Data/Services`, `MyFinance.App/{ViewModels,Views}/Pages` and `/Dialogs`.
- **Testing:** pure logic in `MyFinance.Core.Tests` (milliseconds, no file); service
  behaviour in `MyFinance.Data.Tests` against real encrypted books on disk.

## Design

### Balances are computed

`BalanceCalculator` takes the account's transactions in order and produces the running
column. Nothing persists it. Storing it would mean every back-dated entry had to rewrite
every later row, and the first code path that forgot would leave a register that looks
correct line by line and is wrong in total.

### A total order on rows

Date alone is not an order — several transactions share a day. Rows carry
`SequenceInDay`, and ties beyond that fall back to id. Without this the register reshuffles
between sessions on database row order alone, which a user reads as corruption.

### Filter after, compute before

The running balance is computed over the account's whole history; the filter is applied to
the resulting rows. The opposite order produces a balance column that means nothing — the
common bug in register implementations, and the reason `Filtering_the_register_keeps_the_balance_column_meaningful`
exists as a test.

### Every transaction has at least one split

Even an uncategorized one. Reports then aggregate over a single uniform table with no
special case for "transactions with no split", and the uncategorized worklist is a query
over splits like any other.

### Amount as magnitude plus direction

The editor takes a positive figure and a Payment/Deposit toggle. Entering a transaction
backwards is the commonest data-entry error in a register, and a signed text box invites it.

### Transfers as two rows

Each account's register then balances on its own, and reports exclude transfers with one
predicate. `RegisterService` keeps the legs in step through amount changes, retargeting,
voiding, and conversion back to an ordinary payment; `TransactionValidator` asserts they
cancel.

### Alternatives rejected

- **A stored running balance column.** Fast to read, impossible to keep true.
- **One row per transfer with two account references.** Makes every balance query a special
  case; the first one that forgets is silently wrong.
- **Nullable splits, with a category on the transaction for the simple case.** Two shapes for
  the same fact, and every report has to handle both.
- **Ordering by date and id only.** Looks sufficient until ids are assigned out of date order
  by an import, which they are.

## Project structure

```
src/MyFinance.Core/Primitives/Money.cs               integer cents, allocation, parsing
src/MyFinance.Core/Registers/BalanceCalculator.cs    running balance, ordering, subsets
src/MyFinance.Core/Accounts/AccountListBuilder.cs    grouping, subtotals, grand total
src/MyFinance.Core/Validation/TransactionValidator.cs  split sums, transfer symmetry
src/MyFinance.Core/Entities/{Account,Transaction}.cs
src/MyFinance.Data/Services/AccountService.cs
src/MyFinance.Data/Services/RegisterService.cs       entry, edit, delete, transfer legs
src/MyFinance.Data/Services/ReconcileService.cs
src/MyFinance.App/ViewModels/Pages/{AccountList,Register}PageViewModel.cs
src/MyFinance.App/ViewModels/Dialogs/TransactionEditorViewModel.cs
```

## Risks

- **Transfer legs drifting apart.** The failure would be invisible in either register alone
  and wrong in every total. Contained by `Transfer_pairs_always_net_to_zero_across_the_books`
  running over the whole book rather than over a constructed pair.
- **Balance-only imported accounts.** They exist so migration loses nothing, but they are a
  type the register cannot keep correct. Writes are refused rather than half-supported.
- **Register performance on a large book.** ~20,000 rows is well within reach, but the whole
  history is read to compute the column. If it ever becomes slow, the fix is a windowed
  computation with a carried opening figure — not a stored balance.
