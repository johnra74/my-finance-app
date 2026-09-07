# Feature Specification: Accounts and the register

**Folder:** `002-accounts-and-register`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "Keep my accounts and enter transactions the way Microsoft Money's banking screen
did: a grouped account list with subtotals, and a register with a running balance."

## User Scenarios & Testing

### Primary user story

Someone maintaining their own books wants a list of their accounts with the totals they
care about, and, behind each one, a register of dated transactions with a running balance
they can read straight down the page and compare against a paper statement. They enter
payments and deposits, split one payment across several categories, move money between their
own accounts, and once a month tick off what the bank says happened and confirm the two
agree.

### Acceptance scenarios

1. **Given** several accounts, **When** the account list is shown, **Then** they are grouped
   into Bank and Credit with a subtotal per group and a grand total, and each account shows
   both its bank balance and its current balance.
2. **Given** an account, **When** a transaction is entered, **Then** it appears in date order
   with the running balance recomputed from it onward.
3. **Given** a payment of $120, **When** it is split across three categories, **Then** it
   cannot be saved until the parts sum to exactly $120.
4. **Given** two accounts, **When** a transfer is entered from one, **Then** a matching row
   appears in the other, and the two amounts cancel.
5. **Given** a transfer, **When** either leg is deleted, **Then** both are.
6. **Given** a filtered register, **When** the filter changes, **Then** the balance column
   still reads correctly, because it was computed over the whole history and filtered after.
7. **Given** a bank statement, **When** the user reconciles, **Then** finishing is refused
   until the difference is zero, and the ticked rows are then locked as reconciled.
8. **Given** an account with history, **When** the user deletes it, **Then** they are told
   how much history that destroys before it happens, and the far leg of every transfer it
   held is removed so the other account's balance stays honest.

### Edge cases

- Two transactions on the same day → ordered by an explicit sequence, then id. Without a
  total order the balance column reshuffles between sessions on database row order alone,
  which reads as corruption even when every figure is right.
- A back-dated entry → lands in its correct position, and every later balance moves.
- A voided transaction → stays visible but counts toward no balance at any cleared level.
- A transfer to the same account → refused.
- An account carried over from Money that this version does not model (a loan, a brokerage)
  → shown under its own heading, balance only, and refused as a write target.
- Deleting something already deleted → reported cleanly, not as a crash.
- A transaction with no payee → allowed. Cash entries genuinely have none.

## Requirements

### Functional requirements

**Accounts**

- **FR-001**: The system MUST support creating, editing, closing, reopening and deleting
  accounts, with a unique non-blank name.
- **FR-002**: The system MUST group accounts under the headings the reference application
  used, subtotal each group, and give a grand total.
- **FR-003**: The system MUST leave out groups with nothing in them, rather than showing an
  empty heading.
- **FR-004**: The system MUST report, per account, both the **bank balance** (what has
  cleared) and the **current balance** (everything entered), because they answer different
  questions.
- **FR-005**: The system MUST include the account's opening balance in every balance.
- **FR-006**: The system MUST sort accounts by a manual order, then by name, with closed
  accounts at the bottom of their group.
- **FR-007**: The system MUST freeze an account's type once it has transactions.
- **FR-008**: The system MUST state how much history deleting an account will destroy,
  before doing it.
- **FR-009**: The system MUST remove the far leg of every transfer belonging to a deleted
  account.
- **FR-010**: The system MUST report the count of transactions still needing a category, per
  account and across the book.

**The register**

- **FR-011**: The system MUST list one account's transactions in date order with a running
  balance.
- **FR-012**: The system MUST NOT store running balances. A running balance is a property of
  position in an ordered sequence; storing it would create a second source of truth.
- **FR-013**: The system MUST impose a total order on rows sharing a date, so the register
  reads identically between sessions.
- **FR-014**: The system MUST show payments and deposits in separate columns, split on the
  sign.
- **FR-015**: The system MUST allow filtering by date range, by "needs a category", and by a
  search over payee, memo, number and amount.
- **FR-016**: The system MUST compute the running balance over the account's whole history
  and apply the filter afterwards, so filtering never changes a balance.
- **FR-017**: The system MUST keep voided transactions visible while excluding them from
  every balance.

**Entering a transaction**

- **FR-018**: The system MUST accept an amount as a positive figure plus a
  payment-or-deposit direction, rather than as a signed number.
- **FR-019**: The system MUST give every transaction at least one split, even when
  uncategorized, so reports aggregate over one uniform table with no special cases.
- **FR-020**: The system MUST refuse splits that do not sum to the transaction total.
- **FR-021**: The system MUST replace a transaction's splits on edit rather than accumulating
  them.
- **FR-022**: The system MUST reuse an existing payee rather than creating a duplicate.
- **FR-023**: The system MUST remember, per payee, the category and amount last used — but
  MUST NOT teach a payee a single category from a split transaction, because there was none.
- **FR-024**: The system MUST reject a transaction dated before 1900 as a misread import.
- **FR-025**: The system MUST report every validation failure together, not just the first.

**Transfers**

- **FR-026**: The system MUST record a transfer as two linked rows, one per account, that
  cancel.
- **FR-027**: The system MUST keep both legs in step through every edit — amount, date,
  retargeting to another account, voiding, and conversion back to an ordinary payment.
- **FR-028**: The system MUST refuse a transfer whose two ends are the same account.
- **FR-029**: The system MUST exclude transfers from spending and from the uncategorized
  worklist. Moving your own money is not an expense.

**Reconciling**

- **FR-030**: The system MUST open a reconciliation with everything not yet reconciled, and
  show a running difference against the statement's ending balance.
- **FR-031**: The system MUST refuse to finish a reconciliation whose difference is not zero.
- **FR-032**: The system MUST lock the ticked rows as reconciled on finishing, and start the
  next reconciliation where the last one ended.
- **FR-033**: The system MUST exclude voided rows from a reconciliation entirely.

**Balance-only accounts**

- **FR-034**: The system MUST refuse writes to an account type it does not model, rather
  than accepting entries it cannot keep correct.

### Non-functional requirements

- **NFR-001**: Register load and balance computation MUST stay responsive on a book of
  ~20,000 transactions.
- **NFR-002**: Every failure that names a missing account or transaction MUST be reported as
  a clean, catchable outcome rather than an unhandled fault.

## Key Entities

- **Account** — one register and one running balance. Holds name, type, institution, a
  *masked* account number, opening balance, currency code, closed/favourite flags, manual
  sort order, last reconciled date and balance, and the keyed digest used to recognise its
  statements. Never a full account number.
- **Transaction** — date, sequence-in-day, amount (signed from this account's view), payee,
  memo, cheque number, cleared status, void flag, and an optional link to the far leg of a
  transfer.
- **Split** — one allocation of a transaction to a category. At least one always exists.
- **Payee** — a name, its aliases, and the category and amount last used with it.

## Success Criteria

- **SC-001**: The running balance is the prefix sum of amounts, and the final register
  balance equals the account's current balance. *`BalanceCalculatorTests.Running_balance_is_the_prefix_sum_of_amounts`,
  `Final_register_balance_equals_the_current_balance`.*
- **SC-002**: Register order is identical regardless of the order rows are supplied in.
  *`BalanceCalculatorTests.Register_order_is_stable_regardless_of_input_order`,
  `Transactions_sharing_a_date_are_ordered_by_sequence_then_id`.*
- **SC-003**: A month of running balances on mixed amounts carries its cents correctly and
  does not drift. *`BalanceCalculatorTests.Running_balances_survive_a_whole_month_of_mixed_amounts`.*
- **SC-004**: Account-list subtotals add up within a group and the grand total across them,
  with liabilities signed negative. *`AccountListBuilderTests.Subtotals_add_up_within_a_group_and_the_total_across_them`,
  `The_account_list_groups_and_subtotals_the_way_the_banking_screen_shows_it`.*
- **SC-005**: A credit-card balance is negative while money is owed.
  *`RegisterServiceTests.A_credit_card_balance_is_negative_while_money_is_owed`.*
- **SC-006**: Voided transactions move no balance at any cleared level.
  *`BalanceCalculatorTests.Balances_exclude_voided_transactions_at_every_cleared_level`.*
- **SC-007**: Splits summing to anything other than the total are refused; an allocated split
  always satisfies the invariant. *`TransactionValidatorTests.Splits_that_do_not_sum_to_the_total_are_rejected`,
  `An_allocated_split_always_satisfies_the_sum_invariant`.*
- **SC-008**: Every transfer pair in the book nets to zero.
  *`TransactionValidatorTests.Transfer_pairs_always_net_to_zero_across_the_books`.*
- **SC-009**: A reconciliation cannot be finished while the difference is non-zero, and the
  next one starts where the last finished. *`ReconcileServiceTests.An_unbalanced_reconciliation_is_refused`,
  `A_second_reconciliation_starts_where_the_first_finished`.*
- **SC-010**: Filtering a register never changes a figure in the balance column.
  *`RegisterServiceTests.Filtering_the_register_keeps_the_balance_column_meaningful`.*

## Assumptions

- Depends on constitution principles **1** (integer cents), **2** (sign from the owning
  account; transfers as two rows), **3** (every write through a service), **4** (balance
  arithmetic lives in `MyFinance.Core`, testable without Windows).
- Single currency. Every account carries a currency code but it is always the same one; see
  `015-multi-currency`.
- One person, one open book, one session. No concurrent writers.

## Out of Scope

- Investment transactions — buys, sells, lots, prices. See `013-investment-accounts`.
- Loan amortization schedules.
- Searching across accounts at once; search is per-register today.
- Attachments and receipt images.
- Printing a register. See `016-printing`.
