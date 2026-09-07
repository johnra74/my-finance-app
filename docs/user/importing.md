# Importing a statement

Download a statement from your bank and import it. Reachable from the account list or from
inside a register.

:::{note}
**File import only.** MyFinance never connects to a bank, so it never asks for your online
banking credentials and has nowhere to store them. Direct Connect was considered and
rejected — see {doc}`../dev/principles`.
:::

## Formats

| Extension | What it is |
|---|---|
| `.ofx` | Open Financial Exchange, 1.x (SGML) or 2.x (XML) |
| `.qfx` | OFX with Quicken's extensions. Read as OFX. |
| `.qbo` | OFX with QuickBooks' extensions. Read as OFX. |
| `.qif` | Quicken Interchange Format |

**Which format a file actually is comes from its contents, not its extension**, because
banks label them inconsistently.

One parser reads both OFX dialects: the 1.x SGML that most US banks still emit, with its
optional closing tags and Windows-1252 text, and the 2.x XML form. A malformed or truncated
file is salvaged as far as it goes rather than refused outright, and a bank that rejects
your request has its own message shown back to you instead of a misleading *"0 transactions
found"*.

### QIF is a special case

QIF records no version, no encoding and no locale. The whole file is scanned before
anything is read, to settle whether `03/02/2026` is March or February and whether `1,234`
is one thousand or one point two three four.

What QIF has that OFX does not is **your own categorization, including splits**, and that
comes across intact — offering to create any category your book does not have yet.

:::{note}
A QIF transfer (`L[Savings]`) is imported as an ordinary transaction, not a linked one. A
QIF export covers one account, so the far leg is not in the file. Inventing one would put a
transaction in an account you never imported, and duplicate it when you import that
account's own export.
:::

## What happens

1. **The account is matched.** After the first import, MyFinance recognises which account a
   statement belongs to and the rest is one click. What it stores is a keyed digest of the
   bank and account identifiers — never the account number itself.
2. **Duplicates are found.** The bank's own reference is authoritative, so re-importing an
   overlapping statement adds only what is new. Rows carrying no reference are matched on
   amount, a nearby date and the payee, and shown as **suspected** rather than silently
   dropped.
3. **Payees are cleaned up.** `SQ *BLUE BOTTLE 1234 NEW YORK NY` becomes `Blue Bottle New
   York`. The full text is kept in the memo, and if you correct the name it is remembered
   against the stable part of the descriptor, so the same shop lands on the same payee next
   month.
4. **Categories are suggested.** See {doc}`categorizing`.
5. **You review it.** Nothing is written until you accept the preview.

## The sign check

Credit card issuers disagree about which way round amounts run, and **a statement imported
backwards is the one failure that corrupts a book without announcing itself** — every
figure is plausible and every total is wrong.

So the preview shows the balance the import would produce beside the balance the bank
states, and offers a switch to reverse the file. Look at those two numbers.

## While it runs

Reading a large statement, learning from your history and writing the rows all happen off
the interface thread, with a bar saying which stage it is on and how far through.

Anything slow can be stopped, and **stopping leaves the account exactly as it was** — the
writes run in a transaction, so there is no half-finished import to clean up afterwards.

## Undo

Every import is recorded as a batch. **Import history** lists them and removes any one of
them in a single action.

## Dates and time zones

An OFX timestamp's zone is parsed but deliberately not applied. Converting
`20260101190000[-5:EST]` to UTC would move that transaction to 2 January — desynchronising
your register from the paper statement and breaking duplicate matching against rows
imported earlier.
