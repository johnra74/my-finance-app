# Bringing a Microsoft Money book across

Money's files are Jet 4 databases in the "MSISAM" flavour Money wrote. Reading one needs
**no Microsoft component, no ODBC driver and no copy of Money**: MyFinance reads the file
format directly.

:::{important}
**Migration writes into an empty book only.** Merging twenty thousand transactions into a
book that already has some would make duplicates nobody could untangle afterwards. Do this
first, before entering anything.
:::

## Doing it

From the account list, choose **Bring across a Money file…**. The wizard reads the file,
shows you what is in it, and writes the whole book in one go.

Nineteen thousand transactions takes a little while. It reports each stage — categories,
payees, accounts, transactions, transfers, bills — and can be stopped at any point, which
rolls the whole thing back rather than leaving a partly-migrated book.

:::{note}
**The `.mny` file is only ever read.** It is never opened for writing, moved or changed, so
a failed migration cannot damage the only copy of twenty-five years of records.
:::

## What comes across

- **Accounts, categories, payees and every transaction**, with splits, transfers and
  reconciliation state intact. Money signs an amount from the account's own point of view
  and writes a transfer as two rows that cancel — the same conventions used here — so the
  mapping is a translation rather than a rebuild.
- **Money's three-level category tree becomes two levels**, because its top level is only
  ever the two roots INCOME and EXPENSE, which are recorded here as a flag on the category.
  Nothing is invented and nothing is dropped. Your chart replaces the seeded starter one.
- **Payee memory.** Every decision about which category a shop belongs in, built up over
  decades, becomes the memory that fills in your next imported statement. This is what makes
  a migrated book immediately useful rather than merely complete.
- **Money's merchant-code table** — the curated list mapping a shop's industry code to a
  category, the only thing in the whole chain that can categorize a shop you have genuinely
  never dealt with.
- **Recurring bills.** Series come across with their frequencies, which were verified
  against Money's own Bills screen rather than guessed. A series whose frequency could not
  be verified is **listed for you to set up by hand** rather than converted wrongly — a
  wrong due date is worse than no due date.
- **Investment holdings** — quantities and price history. See the caveat below.
- **Cheque numbers**, decoded. Money stores that field as a sort key: a type flag and then
  the number padded out. It is read back to the number you actually wrote.

## What does not

| Not carried | Why |
|---|---|
| **Investment cost basis** | Money records none this reader can recover, so a migrated holding arrives with its quantity and price history and a cost of zero, and the diagnostics say so. A zero silently presented as a cost would read as though the shares were free. |
| **Account numbers** | Money stores them encrypted, and MyFinance does not keep full numbers anyway. |
| **A password-protected file** | Money encrypts its own catalog in every file, which this reader steps around by identifying tables from their columns. A password on the file encrypts the data too. Remove it in Money and save a copy. |

There is a full table in {doc}`../reference/migration-coverage`.

## Checking it worked

The last step lists every account with its closing balance, laid out to be read straight
against Money's own account list — because Money is still on the machine and still shows it.
**A migration you cannot verify is one you should not trust.**

:::{admonition} Why your balances may look too low
:class: tip

**Money counts the bills it projected into your register but which nobody ever entered.** A
book last used a while ago shows a balance far below its real one, because the bills kept
being projected forward and the salary did not.

Those rows are **kept by default** for exactly that reason: leaving them out gives a tidier
book whose figures no longer match Money's, and then you cannot check the migration at all.
The wizard offers the choice and says which you have picked.
:::

## The options

| Option | Default | Effect |
|---|---|---|
| Include closed accounts | **On** | A closed account still holds the history explaining where the money went. Dropping it changes every total spanning the years it was open. |
| Keep projected-but-never-entered bills | **On** | See the note above. Off gives a tidier book that will not reconcile against Money. |
| Include payees with no transactions | Off | Payees Money marked hidden. |
