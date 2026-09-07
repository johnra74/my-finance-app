# Tasks: Investment accounts

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Data model:** `./data-model.md`
**Status:** **complete**, with `FR-005` partly met — Money records no cost basis to migrate.

New coverage: `QuantityTests` (22), `HoldingCalculatorTests` (15), `InvestmentServiceTests` (16)
and five `.mny`-backed tests — **58 tests**. The suite went from 992 to **1,051**.

Five things differed from the plan and are recorded at the end of `plan.md`; the sharpest is
that net worth needed a *replay* rather than a valuation, or every historical point would have
shown today's holdings.

`[ ]` outstanding · `[P]` may run in parallel with its neighbours.

**Deliberately kept short.** This is the largest gap and the least likely to be built soon; a
forty-task plan for it would be the least accurate document in the repository. The task list
stays at the level the average-cost decision actually supports, and stops.

## Phase 1 — The exact quantity type

- [X] **T001** `Quantity`: a scaled-integer struct mirroring `Money` — exact arithmetic,
  half-away-from-zero rounding, overflow detected rather than wrapped
  - Implements: `src/MyFinance.Core/Primitives/Quantity.cs`
  - Proven by: `QuantityTests` mirroring
    `tests/MyFinance.Core.Tests/Primitives/MoneyTests.cs` case for case —
    `Repeated_addition_stays_exact_where_double_would_drift`,
    `Rounds_half_away_from_zero_not_to_even`, `Overflow_is_detected_rather_than_wrapping`,
    `A_fractional_share_count_round_trips_without_loss`, `Sum_of_empty_sequence_is_zero`,
    `Summing_a_thousand_fractions_is_exact`, `Eight_decimal_places_survive`,
    `A_fraction_of_nothing_is_nothing_rather_than_a_divide_by_zero`
  - **First, because every figure in the feature derives from it**, and because `double` is
    ruled out by constitution 1.
  - Requirement: NFR-001

## Phase 2 — Entities and the arithmetic over them

- [X] **T002** Security, Holding, InvestmentTransaction, SecurityPrice; `AccountType.Brokerage`
  - Implements: `src/MyFinance.Core/Entities/{Security,Holding,InvestmentTransaction,SecurityPrice}.cs`
  - Implements: `src/MyFinance.Core/Enums/Enums.cs` (`AccountType`)
  - Note: loans and mortgages **stay** on `UnsupportedImported`. They are a separate gap.
  - Implements: `src/MyFinance.Core/Entities/Investments.cs` (all four),
    `src/MyFinance.Core/Enums/Enums.cs` (`AccountType.Brokerage`, `SecurityType`,
    `InvestmentActivity`), `src/MyFinance.Data/Configurations/InvestmentConfiguration.cs`
  - `Security` collides with the `MyFinance.Data.Security` namespace and is aliased in the data
    layer rather than renamed — it is the right domain word.
  - Proven by: `InvestmentServiceTests.A_brokerage_account_is_no_longer_balance_only`
  - Requirements: FR-001, FR-002
- [X] **T003** Average cost: a buy adds quantity and cost, a sell removes quantity and a
  proportional share of cost
  - Implements: `src/MyFinance.Core/Investments/HoldingCalculator.cs`
  - Proven by: `HoldingCalculatorTests.A_buy_adds_quantity_and_cost`,
    `A_sell_removes_a_proportional_share_of_cost`,
    `Two_buys_at_different_prices_average_out`,
    `A_holding_sold_to_zero_is_kept_at_zero_with_no_cost`,
    `A_sale_larger_than_the_holding_is_refused`,
    `Selling_everything_in_pieces_releases_the_whole_cost` — the property that catches an
    apportionment that leaks: however a holding is broken up, the cost out equals the cost in
  - Requirements: FR-001, FR-006
- [X] **T004** Valuation, and the honesty about it: value at the latest price **at or before** a
  date, carrying that date; value at cost when there is no price
  - Implements: `HoldingCalculator`
  - Proven by: `HoldingCalculatorTests.A_holding_is_valued_at_the_latest_price_on_or_before_the_date`,
    `A_valued_figure_carries_the_date_of_the_price_behind_it`,
    `A_holding_with_no_price_is_valued_at_cost_and_says_so`,
    `A_price_after_the_valuation_date_is_not_used` (or a 2019 net-worth figure would move every
    time somebody typed this morning's price), and through the service in
    `InvestmentServiceTests.A_valued_figure_carries_the_date_of_the_price_behind_it`
  - Requirements: FR-007, FR-008

## Phase 3 — Recording activity

- [X] **T005** Buys, sells, dividends, reinvestments and fees, each writing its cash leg as an
  **ordinary transaction** through the existing register path
  - Implements: `src/MyFinance.Data/Services/InvestmentService.cs`
  - Reuses: `src/MyFinance.Data/Services/RegisterService.cs`, so the cash leg inherits
    sequencing, the running balance and the split invariant. This is what stops the feature
    becoming a second ledger.
  - Implements: `src/MyFinance.Data/Services/InvestmentService.cs`
  - Proven by: `InvestmentServiceTests.A_buy_decreases_cash_and_increases_the_holding`,
    `A_sell_increases_cash_and_decreases_the_holding`,
    `A_dividend_reaches_the_cash_register`,
    `A_cash_leg_is_an_ordinary_transaction_with_its_splits`,
    `Selling_more_than_is_held_is_refused`, `An_ordinary_account_cannot_hold_securities`,
    `A_security_is_created_once_and_found_again`
  - Requirement: FR-002
- [X] **T006** A migration for the new tables
  - Implements: `src/MyFinance.Data/Migrations/<timestamp>_Investments.cs`
  - Proven by: `InvestmentServiceTests` running against a real book
  - **Bumped the schema version to 4**, and `017`'s interlock caught it: adding the migration
    failed `SchemaUpgradeTests.The_current_schema_version_matches_the_migration_count`
    immediately, which is exactly what that test was built for.
  - One `017` test needed reframing as a result — see `plan.md` note 5.
  - Requirement: FR-001

## Phase 4 — Where holdings show up, and where they must not

- [X] **T007** Net worth includes holdings, with the price date shown
  - Implements: `src/MyFinance.Core/Reporting/ReportEngine.cs`
  - Implements: `HoldingCalculator.StateAt` (new — a **replay**, not a valuation of today's
    holdings) and `ReportEngine.NetWorthOverTime`
  - Proven by: `InvestmentServiceTests.Net_worth_includes_the_value_of_holdings`,
    `Net_worth_before_a_purchase_does_not_include_it` — shares bought in March must not appear
    in a January figure, however early the price was recorded — plus the existing
    `Debt_reduces_net_worth` and
    `Every_movement_up_to_a_date_counts_not_only_those_in_the_window` passing **unmodified**
  - Requirement: FR-003
- [X] **T008** Investment activity excluded from spending and income, by predicate
  - Implements: `ReportEngine`
  - Implements: `ReportEntry.IsInvestment` + the filter in `ReportEntry.Matches`, set from the
    link the investment record already keeps to its cash leg
  - Proven by: `InvestmentServiceTests.A_share_purchase_never_appears_as_spending`,
    `A_dividend_never_appears_as_income`, and
    `An_ordinary_expense_in_a_brokerage_account_still_counts_as_spending` — the exclusion
    follows the investment record, not "anything in a brokerage account"
  - Requirement: FR-004
- [ ] **T009** [P] A holdings view and its editor, showing each value with its price date
  - Implements: `src/MyFinance.App/`
  - ⚠️ **Not built.** `InvestmentService.GetHoldingsAsync` returns everything a view needs —
    quantity, cost, value, the price date and whether it is carried at cost — but no screen
    consumes it yet. Left because it is the one part that needs Windows to check at all, and
    the arithmetic behind it is covered without one.
  - Requirements: FR-007, FR-008

## Phase 5 — Bringing holdings across

- [X] **T010** Read holdings from a `.mny` and migrate them
  - Implements: `src/MyFinance.Import/Mny/MoneyReader.cs`,
    `src/MyFinance.Data/Services/MigrationService.cs`
  - Note: the holdings tables are already reachable by the existing column-signature work in
    `src/MyFinance.Import/Mny/MoneyTables.cs`.
  - Implements: `MoneyTables` (three new signatures), `MoneyReader` (securities, holdings,
    prices), `MigrationService.WriteHoldingsAsync`
  - Proven by: `MoneyReaderTests.Securities_holdings_and_prices_are_read_when_the_file_has_them`,
    `Every_security_has_a_name`,
    `MoneyFileMigrationTests.Holdings_come_across_with_their_quantity`,
    `Recorded_prices_come_across_with_their_dates`,
    `A_migrated_holding_says_that_its_cost_is_unknown_rather_than_zero`
  - ⚠️ **Quantity and prices only.** Money's transaction table has no quantity, price or cost
    column, so there is no cost basis to migrate. It is left at zero and the summary says so —
    the same judgement `009` FR-024 made about due dates. See the closing section of `spec.md`.
  - Requirement: FR-005

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T002, T003, T006 | ✅ |
| FR-002 | T002, T005 | ✅ |
| FR-003 | T007 | ✅ with a historical replay, not today's holdings |
| FR-004 | T008 | ✅ structural, like transfers |
| FR-005 | T010 | ⚠️ **quantity and prices only — Money records no cost basis** |
| FR-006 | T003 | ✅ |
| FR-007, FR-008 | T004 | ✅ logic; ⚠️ T009's screen needs Windows |
| FR-009 | **No task.** Corporate actions are out of scope; satisfied by *not* claiming to handle them, and by every figure stating its basis. | ✅ by construction |
| FR-010 | **No task.** `004-statement-import` already refuses investment statements; nothing changed. | ✅ by construction |
| NFR-001 | T001 | ✅ |
| SC-001 | T010 | ⚠️ **cannot be met from the file** — see `spec.md` |
| SC-002 | T007 | ✅ |
| SC-003 | T008 | ✅ |

## What is not covered

- **Cost basis on migration.** Not in the file. Reported rather than invented.
- **The holdings screen (T009).** The figures are tested; the view is not built, and would
  need Windows to check. The service returns everything it needs.
- **Corporate actions.** Out of scope by decision. A holding that has undergone a split will be
  wrong until the user corrects it, which `FR-009` states rather than hides.

**Collision warning:** `015-multi-currency` also changes `MyFinance.Core/Primitives` and also
touches net worth. The build order puts `015` first; whichever runs second inherits a merge
there.
