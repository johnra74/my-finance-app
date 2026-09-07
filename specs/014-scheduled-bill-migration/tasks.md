# Tasks: Bringing recurring bills across from Money

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** **complete**

The blocker at T001 is resolved. New coverage: `MoneyFrequencyTests` (16),
`MoneyReaderTests` (+4) and `MoneyFileMigrationTests` (+8) — **28 tests**. The suite went from
876 to **922, with nothing skipped**: a restored `.mny` also un-skips the 17 tests that have
no file to run against otherwise.

Four things differed from the plan and are recorded at the end of `plan.md`. Three of them
would have produced a wrong book, and all three were caught by tests.

This feature owns no entities, so there is no `data-model.md`. The only new artefact is the
frequency mapping table, which lives in `plan.md`.

`[ ]` outstanding · `[P]` may run in parallel with its neighbours.

## Phase 0 — The gate

- [X] **T001** ✅ **Resolved.** Mapping derived and verified three ways
  - **Done.** Money's own Bills screen supplied the labels and a reference `.mny` the codes;
    both are gitignored and stayed on the machine. Three pairs cover every series in that
    file; the mapping table is in `plan.md`.
  - Implements: `src/MyFinance.Import/Mny/MoneyFrequency.cs`
  - Proven by: `MoneyFrequencyTests` — 16 cases, including
    `A_count_per_period_is_never_carried_through_as_an_interval` and
    `A_code_with_no_verified_meaning_maps_to_unknown`
  - Produces: rows in the mapping table in `plan.md`. Partial evidence produces partial rows,
    which is a useful outcome and not a failure.
  - Proven by: `MoneyFrequencyTests.Monthly_is_frq_three_with_one_per_period`,
    `Twice_a_month_is_frq_three_with_two_per_period`, `Quarterly_is_frq_four`, and
    `MoneyFileMigrationTests.Every_converted_bill_has_a_frequency_that_was_verified_not_guessed`
  - The table is page 5285 in the reference file, identified by the signature
    `hbill, hbillHead, frq, cFrqInst, cDaysAutoEnter` — `frq`/`cFrqInst` alone would not do,
    since budgets, accounts and securities all carry that pair too.
  - Requirement: NFR-001

## Phase 1 — Read the definitions

- [X] **T002** Read the recurring-bill rows into the Money model
  - Changes: `src/MyFinance.Import/Mny/MoneyModels.cs`, `src/MyFinance.Import/Mny/MoneyReader.cs`
  - Note: the table is already located and its rows already counted by the existing signature
    work in `src/MyFinance.Import/Mny/MoneyTables.cs`. This exposes them rather than finding
    them.
  - Proven by: `MoneyReaderTests.Recurring_bill_definitions_are_read_with_their_amount_and_account`,
    `A_recurring_bill_is_never_also_counted_as_a_transaction`,
    `A_series_that_never_ends_has_no_end_date`,
    `Every_recurring_bill_carries_a_repeat_pattern_or_says_it_could_not`
  - The template is `lHtrn`, **not** `hbillHead` — see the plan's note 2. Reading the wrong one
    produced the wrong payee for almost every bill.
  - Requirement: FR-001
- [X] **T003** The mapping, with **unknown as a real answer** rather than a fallback
  - Creates: `src/MyFinance.Import/Mny/MoneyFrequency.cs`
  - Proven by: `MoneyFrequencyTests.A_code_with_no_verified_meaning_maps_to_unknown`,
    `Every_mapped_code_maps_to_exactly_one_frequency`, `A_missing_code_maps_to_unknown`,
    `A_fractional_count_rounds_rather_than_being_refused`
  - Requirements: FR-001, FR-002

## Phase 2 — Convert, inside the existing migration

- [X] **T004** A bills stage in the migration: convert what is mapped, through `ScheduleService`
  - Changes: `src/MyFinance.Data/Services/MigrationService.cs`
  - Reuses: `src/MyFinance.Data/Services/ScheduleService.cs` (the write path and its
    invariants), `src/MyFinance.Core/Scheduling/RecurrenceRule.cs`
  - Proven by: `MoneyFileMigrationTests.Recurring_bills_come_across_as_scheduled_transactions`,
    `A_twice_monthly_series_is_never_turned_into_a_two_monthly_one`,
    `Every_converted_bill_points_at_an_account_that_can_hold_one`
  - Requirements: FR-001, NFR-001
- [X] **T005** ⚠️ Seed each converted series' occurrence history from the transactions that
  already came across, so nothing is entered twice
  - Changes: `MigrationService`
  - Reuses: the occurrence history from `006-scheduled-bills` FR-018 — no new machinery.
  - Proven by: `MoneyFileMigrationTests.A_migrated_bill_does_not_re_enter_occurrences_already_in_the_register`,
    `Auto_entry_on_a_freshly_migrated_book_never_pays_a_backdated_bill_again`,
    `Running_auto_entry_twice_on_a_migrated_book_adds_nothing_the_second_time`
  - Occurrences Money generated are **entered**; those it never generated are **skipped**.
    Without the second state the first auto-entry wrote the whole backlog as transactions
    that never happened.
  - **This is the expensive failure in the feature.** A series that does not know its
    occurrences were already entered pays every backdated bill again the first time the book
    is opened.
  - Requirement: FR-003
- [X] **T006** Refuse a definition whose account arrived balance-only, and put it on the manual
  list with its reason
  - Changes: `MigrationService`
  - Proven by: `MoneyFileMigrationTests.Every_converted_bill_points_at_an_account_that_can_hold_one`
  - Requirement: FR-005

## Phase 3 — Reporting what was and was not done

- [X] **T007** Report converted and unconverted counts in the migration summary, naming each
  unconverted definition by payee, amount and account
  - Changes: `src/MyFinance.Data/Services/MigrationModels.cs`, `MigrationService`
  - Proven by: `MoneyFileMigrationTests.Every_bill_is_either_converted_or_listed_for_manual_setup`
  - Requirements: FR-002, FR-004
- [X] **T008** [P] The summary page shows the manual list as something the user can work
  through
  - Changes: `src/MyFinance.App/` — the migration wizard's final page
  - Proven by: the content by T007. **Needs Windows** for the layout.
  - Requirement: FR-004

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T002, T003, T004 | ✅ |
| FR-002 | T003, T007 | ✅ |
| FR-003 | T005 | ✅ |
| FR-004 | T007, T008 | ✅ content tested; ⚠️ the summary page needs Windows |
| FR-005 | T006 | ✅ |
| NFR-001 | T001, T004 | ✅ verified three ways against a real file |
| SC-001 | T004 | ✅ |
| SC-002 | T005 | ✅ |
| SC-003 | T007 | ✅ |

**Shippable with a partial mapping**, and it still is: every code without a verified row falls
to FR-002 and is listed for manual setup. On the reference file nothing falls through — all 46
series convert.

## What is not covered

- **The migration summary's manual list as it appears on screen.** Its content is tested; the
  layout needs Windows.
- **The eight unmapped frequencies.** Never seen in a real file, so never verified. They are
  handled — reported, not guessed — but the handling has only been exercised synthetically.
