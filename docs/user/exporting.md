# Taking your data with you

Money's file format is the reason this application exists. **A MyFinance book that could
only be read from inside MyFinance would be the same trap with a different lock.**

## Any report, as CSV

At any time, from the report itself.

## The whole book, as JSON

**Export** in the top bar writes every account, category, payee, transaction, split,
transfer, scheduled bill, budget, categorization rule and payee memory to a single file that
can be read without this application.

- **One file, with a documented shape.** It carries a format version, and every id it
  references resolves to something in the file — transfers name their far leg rather than
  leaving you to pair rows up by amount and date, which is the specific thing QIF cannot do.
- **Amounts appear twice**: `"amount": "-42.50"` beside `"amountMinorUnits": -4250`. The
  string is for you and your spreadsheet; the integer is for a program, because written as a
  bare JSON number it would become a floating-point value in most readers and stop being
  exact.
- **Nothing secret and nothing derived** goes in — no account digests, no per-book secret,
  no cached payee vectors, no running balances. Only what you actually entered.

:::{danger}
**The export is not encrypted.** Anyone who can read the file can read your entire financial
history. MyFinance says so before you choose where to put it. Keep it where you would keep a
bank statement.

This is deliberate. Encrypting it would recreate the problem it exists to solve.
:::

:::{warning}
**Nothing reads the export back in.** It is a way to get your data out and into a
spreadsheet or another program — it is not a backup, and it cannot restore a book. For that,
see {doc}`backups`.
:::

The full shape is documented in `specs/012-full-book-export/data-model.md`, and a test
asserts the document matches it, so the two cannot drift apart.
