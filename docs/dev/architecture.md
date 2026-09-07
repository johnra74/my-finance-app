# Architecture

## The projects

```text
src/MyFinance.Core       net10.0           domain model, Money, register and balance logic,
                                           recurrence, cash-flow projection, the report
                                           engine, budget arithmetic, chart geometry,
                                           holding arithmetic, diagnostics, threading
src/MyFinance.Data       net10.0           EF Core + SQLCipher, schema, migrations, services
src/MyFinance.Import     net10.0           OFX and QIF parsing, payee cleanup, dedupe, sign
                                           analysis, rules, category suggestion, and the
                                           Jet 4 / MSISAM reader for .mny files
src/MyFinance.Semantics  net10.0           the local embedding model, isolated so the other
                                           projects stay pure and quick to test
src/MyFinance.App        net10.0-windows   WPF shell, views, view models
tests/                   net10.0           xUnit + Shouldly
```

**Only `MyFinance.App` targets Windows.** `Directory.Build.props` sets
`EnableWindowsTargeting`, so it *compiles* on Linux and CI can gate on the whole solution —
it just cannot run there. That split is deliberate: it keeps the logic that has to be correct
out of the layer that can only be verified by eye.

## Which way the dependencies point

```text
App  ->  Data  ->  Import  ->  Core
          |          |
          +----------+-------->  Core
                     |
Semantics <----------+
```

`MyFinance.Import` has **no database dependency at all**. Parsing, payee cleaning, duplicate
detection and sign analysis are pure functions over plain snapshots, so they are tested in
milliseconds with no encrypted file involved. The orchestration that touches the database
lives in `Data/Services/ImportService.cs`.

## The data layer

EF Core over SQLite, encrypted with SQLCipher. A `Book` owns the connection; services take an
`IBookContextFactory` and create their own short-lived context per call — which is what makes
it safe to run a whole service call on a pool thread.

`Money` is a `readonly record struct` over a `long` count of minor units, mapped by a global
`ValueConverter<Money, long>` convention, so every amount column is a SQLite `INTEGER`.

## The interface layer

MVVM, using `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`,
`[RelayCommand]`). Pages are singletons so returning to a section keeps its scroll position
and filters; each reloads in `OnNavigatedToAsync`. The register is transient, because it is
opened against a particular account.

### Long work, and getting back from it

`BusyViewModel.RunBusyAsync` is where the three things that must happen together live:
getting off the interface thread, reporting progress, and being stoppable. Getting two of
three right is what produced the frozen window it replaces.

:::{important}
**`ConfigureAwait(true)` is not a guarantee.** It resumes on whatever synchronization context
happened to be current at the await, and on none it resumes wherever the work finished. From
a fire-and-forget call site — of which there are twenty-odd — it is frequently wrong, and the
failure is a bound collection rebuilt on a pool thread, thrown where nobody is listening.

Work that returns to the interface marshals explicitly through
`MyFinance.Core.Threading.UiContext`, captured once at startup. This is principle 9.
:::

## Diagnostics

Three hooks, because there are three ways an exception leaves: the dispatcher, a background
thread, and an unobserved task. Only the first existed before spec 018, and it recorded
nothing.

The log's API takes an **enum**, not a string. With 59 `catch` blocks in the solution, a rule
each of them must remember is a rule that will be broken — so the writer offers no parameter
that could carry a payee, an amount or a path, and redaction is something the compiler
enforces. That is also why `Microsoft.Extensions.Logging` was removed rather than used: its
whole shape is `LogError("failed for {Payee}", payee)`.

## Conventions worth knowing before you change anything

- Transaction amounts are **signed from the owning account's perspective**: negative is money
  out. Credit-card balances are negative while a balance is owed, matching Money.
- A transfer is **two linked rows**, one per account, so each register balances on its own
  and reports can exclude transfers wholesale.
- **Every transaction has at least one split**, even when uncategorized, so reports aggregate
  over one uniform table with no special cases.
- **Running balances are never stored.** They are a property of position in an ordered
  sequence; persisting them would create a second source of truth.
- Rows sharing a date are ordered by `SequenceInDay`, then id. Without a total order the
  balance column reshuffles between sessions on database row order alone, which reads as
  corruption even when every figure is right.
- **Filtering the register never changes the balance column.** The running balance is computed
  over the whole history and the filter applied afterwards.
- **Categories that have been used cannot be deleted**, only archived or merged.
- **List grids are views, not editors.** A `DataGridCheckBoxColumn` binds TwoWay by default;
  over a read model it either throws or silently edits a detached entity. Every one must
  declare `IsReadOnly` or an explicit `Mode`, and a test enforces it.
- An imported transaction arrives **cleared** — it has reached the bank by definition.
- An OFX timestamp's zone is parsed but deliberately **not applied**. Converting
  `20260101190000[-5:EST]` to UTC would move the transaction to 2 January, desynchronising
  the register from the paper statement and breaking duplicate matching.
- The classifier is **retrained from scratch on every import** rather than stored. A personal
  book is small enough that this costs one query, and the model then always reflects every
  correction since the last import — which a persisted model would not without invalidation
  logic nobody would remember to write.
- Every occurrence of a recurring bill is computed **from the series start date and its
  position**, never from the occurrence before it.
