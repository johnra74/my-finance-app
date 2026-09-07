# Accounts and the register

Everything here is under the **Banking** tab.

## The account list

Accounts grouped into Bank and Credit, each group subtotalled, with a grand total below.
Every account shows two figures:

- **Bank balance** — what has cleared. What the bank would tell you today.
- **Current balance** — every transaction you have entered, cleared or not.

They differ by whatever is still in flight, and both are worth seeing: one reconciles
against a statement, the other tells you what you actually have.

You can add, edit, close, reopen and delete accounts. Closing keeps the history and hides
the account; deleting destroys it.

:::{warning}
Deleting an account says how much history it will destroy before it does anything, and
removes the far leg of every transfer so the other account's balance stays honest. There is
no undo. Close it instead unless you are certain.
:::

## The register

One account's transactions in date order, with a running balance.

| Column | |
|---|---|
| Date | |
| Number | A cheque number or a reference such as `ATM` |
| Payee | With autocomplete over the payees you already have |
| Category | Or *Split* |
| Payment / Deposit | Two columns rather than one signed figure |
| Cleared | Click it to toggle |
| Balance | Running, computed over the account's whole history |

The footer carries the bank balance and the current balance.

**Filtering never changes the balance column.** The running balance is computed over the
account's entire history and the filter applied afterwards, so a filtered view still shows
true balances rather than a total of what happens to be on screen.

Filter by date range, by "needs a category", or by searching payee, memo, number and amount.

### Keyboard

| Key | |
|---|---|
| {kbd}`Ctrl` + {kbd}`N` | New transaction |
| {kbd}`Enter` | Edit the selected row |
| {kbd}`Delete` | Delete the selected row |
| {kbd}`F5` | Reload |
| Double-click | Edit |

## Entering a transaction

The editor takes a date, number, payee, category, memo, and the cleared and void flags.

**The amount is a positive figure plus a Payment or Deposit direction**, not a signed
number. That removes the commonest way to enter a transaction backwards — a minus sign in
the wrong place, in a column where the sign convention is not obvious.

When you leave the payee field and the category is still blank, MyFinance fills it in or
offers a suggestion, and says why. It never argues with a category you have already set.
See {doc}`categorizing`.

## Splits

Spread one transaction over as many categories as you like. A live **unassigned** figure
has to reach zero before the split can be saved, so a split can never quietly fail to add
up to its transaction.

Under the covers every transaction has at least one split even when it looks
uncategorized, which is why reports never need a special case.

## Transfers

Pick another account and both legs are written, kept in step through every edit, and
removed together.

Transfers are **not** spending and **not** income. They never appear in a spending report
and never sit in the uncategorized worklist — moving your own money between your own
accounts is not a transaction with a category.

## Voiding and deleting

**Void** keeps the row and takes it out of the balance — a cheque you wrote and cancelled.
**Delete** removes it entirely. Void when something happened and then did not; delete when
it never happened at all.

## Reconciling

Enter the statement date and its ending balance, tick off what appears on it, and watch the
difference. **Finishing is refused until the difference is zero**, because a reconciliation
that does not balance has not reconciled anything.

Imported transactions arrive already cleared — they have reached the bank by definition —
so they are pre-ticked when you open the reconcile screen.
