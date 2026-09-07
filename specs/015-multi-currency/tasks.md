# Tasks: More than one currency

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Data model:** `./data-model.md`
**Status:** **partly complete** — Phases 1, 2, 4 and 5 built; Phase 3 deferred, Phase 6 blocked behind it

`[ ]` outstanding · `[P]` may run in parallel with its neighbours.

**The organising rule: the whole suite is green after every task, not only at the end.**
This changes the type all 836 tests depend on, and the only way to keep the change reviewable
is to hold `SC-004` — *a single-currency book behaves exactly as it does today* — as an
invariant at each step rather than as a final criterion. **A task that leaves the suite red is
not finished.** That held: the suite went 950 → **992**, and every pre-existing test passed
**unmodified** at every step.

## Phase 1 — A currency type nothing consumes yet

- [X] **T001** `Currency`: code, minor-unit exponent, display format, and `Currency.Default`
  - Creates: `src/MyFinance.Core/Primitives/Currency.cs`
  - Implements: `src/MyFinance.Core/Primitives/Currency.cs`
  - Proven by: `CurrencyTests.A_currency_knows_its_minor_unit_exponent`,
    `Yen_has_no_minor_units`, `A_three_decimal_currency_is_expressible`,
    `An_unknown_code_is_taken_at_two_places_rather_than_refused`,
    `The_default_is_the_currency_every_existing_amount_is_in`
  - Requirement: FR-005
  - Suite after this task: **unchanged** — nothing consumes it.

## Phase 2 — Money learns about currency, and nothing changes yet

- [X] **T002** `Money` carries a `Currency`, defaulting to `Currency.Default`
  - Changes: `src/MyFinance.Core/Primitives/Money.cs`
  - Proven by: every test in `tests/MyFinance.Core.Tests/Primitives/MoneyTests.cs` passing
    **unmodified** — none needed editing — plus
    `MoneyCurrencyTests.An_amount_that_was_never_told_is_in_the_default_currency`
  - ⚠️ Two things the plan did not foresee: **zero had to become currency-agnostic** (a sum has
    to start somewhere), and **equality had to be hand-written** (a record struct would compare
    the currency field, and an unstamped zero is everywhere). Both are in `plan.md`.
  - Requirement: FR-001
- [X] **T003** Scale comes from the currency, not from `MinorUnitsPerUnit = 100`
  - Changes: `Money.FromDecimal`, `ToDecimal`, `FromUnits`, formatting, `TryParse`
  - Proven by: `MoneyTests` unmodified, plus
    `MoneyCurrencyTests.A_yen_amount_has_no_minor_units_and_round_trips`,
    `A_three_decimal_amount_round_trips`, `Whole_units_scale_by_the_currency`,
    `A_dollar_amount_is_unchanged_by_any_of_this`
  - Requirements: FR-005, NFR-001, NFR-002
- [X] **T004** Mixed-currency arithmetic and comparison **throw**
  - Changes: `Money` operators, `CompareTo`, `Sum`
  - Proven by: `MoneyCurrencyTests.Adding_two_currencies_throws_rather_than_returning_a_number`,
    `Comparing_two_currencies_throws`, `Sorting_a_mixed_list_is_caught_rather_than_faked`,
    `Summing_a_mixed_sequence_is_refused`, `Zero_absorbs_into_any_currency`,
    `Summing_an_empty_sequence_is_still_zero`,
    `An_unstamped_zero_equals_an_explicitly_stamped_one`,
    `Two_currencies_are_never_equal_however_equal_their_numbers`,
    `Arithmetic_keeps_the_currency`, `Allocation_keeps_the_currency_and_still_loses_nothing`
  - Not silent coercion, not the left operand, not zero — a silent answer here is the same bug
    this spec exists to close, one layer deeper.
  - Requirements: FR-001, FR-002
  - Suite after this phase: **unchanged**. Nothing mixes currencies yet.

## Phase 3 — Persistence · ⛔ **deferred, and nothing below it can proceed**

**Not built, deliberately.** The plan assumed a currency column beside each amount was a small
change. It is not: EF Core maps `Money` through a value converter to one `INTEGER`, and a
converter cannot carry two columns into one struct. Combining them needs a materialization
interceptor that re-stamps every amount after load, or a schema-shaped rewrite that breaks the
eight queries currently translating `Money` comparisons and sums into SQL.

That is a schema fork with two defensible shapes — a column per amount-bearing entity, or a
currency derived from the owning account — and a migration is expensive to reverse, which is
what `017-book-schema-upgrade` exists to make survivable rather than free. It buys nothing
until `T012` allows a foreign account, so it is left for whoever needs one.

- [ ] **T005** Store the currency beside the minor units on every row carrying an amount
  - Changes: `src/MyFinance.Data/Configurations/*.cs`
  - Note: **not** only on the account. Storing it there is smaller and reintroduces exactly
    the problem — an amount whose currency the arithmetic cannot see.
  - Proven by: `BookFileServiceTests.Money_survives_a_save_and_reload_exactly` unmodified, plus
    `A_yen_amount_survives_a_save_and_reload`
  - Requirement: FR-001
- [ ] **T006** A migration defaulting every existing row to the book's single currency
  - Creates: `src/MyFinance.Data/Migrations/<timestamp>_AmountCurrency.cs`
  - Exact rather than a guess: an existing book is single-currency by definition.
  - Proven by: `MultiCurrencyMigrationTests.An_existing_book_upgrades_with_every_amount_in_its_own_currency`
  - Requirement: FR-001
  - **Bumps the schema version** — see `specs/017-book-schema-upgrade` T001.

## Phase 4 — Totals become per-currency groups

- [X] **T007** Account list: subtotals per currency, and **no combined grand total** when more
  than one is present
  - Changes: `src/MyFinance.Core/Accounts/AccountListBuilder.cs`
  - Implements: `src/MyFinance.Core/Accounts/CurrencyTotals.cs`, and `Subtotals` / `Totals` /
    `IsSingleCurrency` on `src/MyFinance.Core/Accounts/AccountSummary.cs`
  - Proven by: every `AccountListBuilderTests` case passing **unmodified** (one currency ⇒ one
    group ⇒ today's output), plus
    `CurrencyTotalsTests.Two_currencies_produce_two_subtotals_and_no_grand_total`,
    `One_currency_produces_one_total_that_reads_as_the_grand_total_always_did`,
    `The_books_own_currency_is_listed_first`, `A_group_reports_its_own_currencies`,
    `An_empty_list_has_no_totals_and_is_still_single_currency`
  - Asking for a grand total across currencies **throws** rather than returning a figure
    nobody could defend. `Totals` is the form that always works.
  - Requirements: FR-002, SC-003, SC-004
- [X] **T008** [P] Registers show their own account's currency
  - Changes: `src/MyFinance.Core/Registers/BalanceCalculator.cs`, and the register view
  - Proven by: `BalanceCalculatorTests` unmodified — a register's balances are already sums of
    that account's own amounts, so they carry its currency with no change needed.
  - ⚠️ **The register does not yet display a currency symbol other than the machine's.**
    Nothing can reach that state until `T012`, so it is left with the persistence question.
  - Requirement: FR-003
- [X] **T009** [P] Reports group per currency and say which currency each figure is in
  - Changes: `src/MyFinance.Core/Reporting/ReportEngine.cs`
  - Proven by: `ReportEngineTests` and `ReportServiceTests` unmodified. `CurrencyTotals.Of` is
    the shared shape a report would group by.
  - ⚠️ **Partly done.** Reports sum within one currency correctly and would now *throw* rather
    than silently add two — which is FR-002's prohibition, loudly. Grouping a report's output
    per currency is not built, because no report can span two until `T012`.
  - Requirements: FR-002, FR-006
- [X] **T010** [P] Budgets and net worth, per currency
  - Changes: `src/MyFinance.Core/Budgeting/BudgetCalculator.cs`, the net-worth path in
    `ReportEngine`
  - Proven by: `BudgetCalculatorTests` unmodified.
  - ⚠️ **Same position as T009**: correct within a currency, refuses across two, not yet
    grouped per currency. Blocked on the same thing.
  - Requirement: FR-002

## Phase 5 — The invariant that has to change

- [X] **T011** ⚠️ Transfer cancellation checked **within** a currency; pairing checked
  structurally across currencies
  - Changes: `src/MyFinance.Core/Validation/TransactionValidator.cs`
  - Proven by: `TransactionValidatorTests.Transfer_pairs_always_net_to_zero_across_the_books`
    **kept exactly as strong as it is today for same-currency pairs**, plus
    `CrossCurrencyTransferTests.A_cross_currency_transfer_pair_is_valid_without_cancelling`,
    `A_cross_currency_pair_must_still_name_each_other_and_share_a_date`,
    `A_same_currency_pair_must_still_cancel_exactly`,
    `Both_legs_pointing_the_same_way_is_refused_whatever_the_currencies`,
    `Validating_a_cross_currency_pair_never_throws_on_the_comparison`
  - The plan said "weaken cancellation". It needed a **third rule** as well, or a
    cross-currency pair would have had no arithmetic check at all: money left one account, so
    it must have arrived in the other.
  - This deliberately weakens one of the strongest assertions in the codebase. It must be a
    visible act in review, not a quiet relaxation of a predicate.
  - Requirement: FR-004

## Phase 6 — Only now, let a second currency in · ⛔ **blocked by Phase 3**

- [ ] **T012** Allow an account to be created in another currency
  - Changes: `src/MyFinance.Data/Services/AccountService.cs`, and the account editor
  - Proven by: `AccountServiceTests.An_account_can_be_created_in_another_currency`,
    `An_accounts_currency_is_frozen_once_it_has_transactions` — following the existing
    `The_type_is_frozen_once_transactions_exist`
  - Requirement: FR-003
  - **Last on purpose.** Admitting a second currency before the totals can express one is how
    a book acquires a number nobody can defend.

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T002, T004 built; T005/T006 deferred | ⚠️ in memory only |
| FR-002 | T004, T007 | ✅ for the account list; ⚠️ reports/budgets refuse but do not group |
| FR-003 | T008 | ⚠️ balances carry their currency; display and T012 deferred |
| FR-004 | T011 | ✅ |
| FR-005 | T001, T003 | ✅ |
| FR-006 | T009 | ⚠️ no rates exist, which is the requirement; grouping deferred |
| NFR-001 | T003 | ✅ integer throughout, scale from the currency |
| NFR-002 | The suite went 950 → 992 with every pre-existing test **unmodified** | ✅ |
| SC-001 | Every phase | ✅ **the central assertion, and it held** |
| SC-002 | T004 | ✅ |
| SC-003 | T007 | ✅ |
| SC-004 | T007 | ✅ |

## What is not built

- **T005 / T006 — persistence.** Deferred; the reasoning is at the head of Phase 3 and in
  `spec.md`.
- **T012 — creating an account in another currency.** Blocked behind persistence, and last by
  design. This is what keeps the half-built feature harmless: **no book can contain a second
  currency**, so none of the gaps above can be reached.
- **Per-currency grouping in reports and budgets.** They refuse to add across currencies —
  which is the prohibition FR-002 actually states — but do not yet present per-currency
  results. Nothing can produce a mixed report until T012.

**Collision warning:** `013-investment-accounts` also changes `MyFinance.Core/Primitives`,
adding an exact quantity type. Whichever of the two runs second inherits a merge there. The
build order in the project plan puts this one first for that reason.
