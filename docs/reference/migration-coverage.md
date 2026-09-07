# What a Money migration carries

The full answer to *"will I lose anything?"*. See {doc}`../user/migrating` for how to do it.

## Carried

| | Notes |
|---|---|
| **Accounts** | Including closed ones by default |
| **Categories** | Money's three levels become two plus an income/expense flag. Its top level is only ever INCOME and EXPENSE, so nothing is invented or dropped. |
| **Payees** | Folded where they normalize alike |
| **Payee memory** | Each payee's last category — decades of decisions, which is what makes a migrated book immediately useful |
| **Merchant codes** | Money's curated industry-code table |
| **Transactions** | Every one, with its date, amount, memo and cleared state |
| **Splits** | Intact, and they add up |
| **Transfers** | Both legs linked to each other |
| **Reconciliation state** | |
| **Cheque numbers** | Decoded from Money's sort key — see below |
| **Recurring bills** | Frequencies verified against Money's own Bills screen. Anything unverifiable is **listed for manual setup**, not converted. |
| **Investment holdings** | Quantities and price history |

## Not carried

| | Why |
|---|---|
| **Investment cost basis** | Money records none this reader can recover. A holding arrives with quantity and prices and a cost of **zero**, and the diagnostics say so — a zero presented silently as a cost would read as though the shares were free. |
| **Account numbers** | Money stores them encrypted, and MyFinance keeps no full numbers anyway. |
| **A password-protected `.mny`** | Money encrypts its own catalog in every file, which this reader steps around by identifying tables from their columns. A password on the file encrypts the data too. Remove it in Money and save a copy. |

## Three traps worth knowing about

These were found by verifying against a real file rather than by reading a specification, and
each would have produced a plausible, wrong book:

**Balances that look too low.** Money counts the bills it projected into the register but
which nobody entered. A book left unused shows a balance far below its real one, because the
bills kept projecting forward and the salary did not. Those rows are kept by default so the
migrated figures match Money's and the migration can actually be checked.

**Bill frequencies.** Money's `cFrqInst` is a **count per period**, not a multiplier — so
`(monthly, 2)` means twice a month, not every two months. And one bill is several rows: Money
writes a new one each time the terms change, so most of the table is revision history.

**Phantom payments.** A due date Money never generated must be recorded as *skipped* rather
than left outstanding. Marking only the generated ones left a backlog of phantom payments, which
the first auto-entry would have written into the register as a decade of payments that never
happened.

## Cheque numbers

Money stores that field as a sort key rather than as what you typed: a one-character type
flag, then — for a number — the digits right-aligned in a twelve-character field, so that
sorting the column as plain text orders cheques numerically and puts references such as `ATM`
after them.

| Flag | Stored | Read as |
|---|---|---|
| `0` | `0        1168` | `1168` |
| `1` | `1ATM` | `ATM` |

The decoding is deliberately narrow, because a cheque numbered `1234` by hand also begins
with a `1`: the flag is believed only where the remainder could not be the value itself.

Books migrated before this was understood are repaired by the schema-5 upgrade. See
{doc}`../dev/schema-migrations`.

## Guarantees

- **The `.mny` file is only ever read** — never opened for writing, moved or changed.
  Principle 11.
- **Migration writes into an empty book only.**
- **Stopping rolls the whole thing back**, rather than leaving a partly-migrated book.
- **The last step lists every account's closing balance**, laid out to be read straight
  against Money's own account list. A migration you cannot verify is one you should not
  trust.
