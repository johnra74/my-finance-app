# Glossary

:::{glossary}

Argon2id
  The function that turns your password into the key that decrypts your book. Deliberately
  slow and memory-hungry — 4 iterations, 64 MiB — so that guessing it offline is expensive.

Bank balance
  What has cleared: the figure the bank would give you today. Contrast {term}`current
  balance`.

Book
  One person's complete financial records: the {{ book_ext }} database and its
  {{ sidecar_ext }} sidecar, which are a pair.

Cleared
  A transaction the bank has seen. Imported transactions arrive cleared, because reaching the
  bank is what importing a statement means.

Current balance
  Every transaction entered, cleared or not. Differs from the {term}`bank balance` by
  whatever is still in flight.

Descriptor
  The raw text a bank puts on a statement line — `SQ *BLUE BOTTLE 1234 NEW YORK NY`. Cleaned
  into a {term}`payee` and kept in full in the memo.

FITID
  The bank's own reference for a transaction, carried in an OFX file. Authoritative for
  duplicate detection.

Jet 4 / MSISAM
  The database format Microsoft Money wrote. MyFinance reads it directly — no Microsoft
  component, no ODBC driver, no copy of Money.

Money (the type)
  An integer count of cents, stored as a SQLite `INTEGER`. No binary floating point is
  involved anywhere in this application.

OFX
  Open Financial Exchange, the format most banks offer. Two incompatible dialects: 1.x SGML
  and 2.x XML. Both are read by one parser.

Payee
  Who was paid or who paid you. Names are normalized — case, punctuation and spacing collapse
  — so several spellings of one shop resolve to one payee.

Payee memory
  The category a payee was last filed under. The third and most reliable of the six
  {term}`suggestion` sources, and the thing that makes a migrated book immediately useful.

QIF
  Quicken Interchange Format. Records no version, no encoding and no locale, so the whole file
  is scanned before anything is read.

Rollover
  A budget category carrying unspent money into next month. An overspend is never carried
  forward.

Schema version
  One integer per migration, recorded in both the database and the sidecar. What lets a book
  written by a newer build be refused rather than silently damaged.

Sidecar
  The {{ sidecar_ext }} file. Holds the Argon2id salt, its cost parameters and the schema
  version. No secret, but losing it is as fatal as forgetting the password.

Split
  One transaction spread over several categories. Every transaction has at least one, even
  when uncategorized, so reports need no special cases.

Suggestion
  A category MyFinance proposes. Six sources in descending order of authority, every guess
  marked as one, and none applied unseen.

Transfer
  Money moved between two of your own accounts, written as **two** linked rows. Never
  spending, never income.

Void
  A transaction that happened and then did not — a cancelled cheque. The row stays; the amount
  leaves the balance. Contrast deleting, for something that never happened at all.
:::
