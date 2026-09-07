# File formats

## Files MyFinance writes

| Extension | | Encrypted? | Needed to open a book? |
|---|---|---|---|
| {{ book_ext }} | The book: a SQLCipher-encrypted SQLite database | Yes | **Yes** |
| {{ sidecar_ext }} | Sidecar holding the Argon2id salt, cost parameters and schema version | No | **Yes** |
| {{ backup_ext }} | An archive holding both of the above | The database inside still is | — |
| `.json` | A whole-book export | **No** | No |
| `.csv` | A report | No | No |
| `.log` | Diagnostics, in `%LOCALAPPDATA%\MyFinance\logs` | No | No |

:::{danger}
{{ book_ext }} and {{ sidecar_ext }} are **a pair**. Either one alone is useless. The sidecar
holds no secret — a salt's job is to be unique, not hidden — but without it the key cannot be
re-derived, and the book is as lost as if the password had been forgotten.
:::

## Files MyFinance reads

| Extension | | Written to? |
|---|---|---|
| `.ofx` | Open Financial Exchange, 1.x SGML or 2.x XML | Never |
| `.qfx` | OFX with Quicken's extensions | Never |
| `.qbo` | OFX with QuickBooks' extensions | Never |
| `.qif` | Quicken Interchange Format | Never |
| `.mny` | Microsoft Money — a Jet 4 / MSISAM database | **Never.** Principle 11. |

Which format a file actually is comes from its **contents**, not its extension.

## The whole-book export

One JSON document with a format version. Every id it references resolves to something inside
the file — a transfer names its far leg rather than leaving you to pair rows by amount and
date, which is the specific thing QIF cannot do.

**Amounts appear twice**, as siblings, never nested:

```json
{
  "amount": "-42.50",
  "amountMinorUnits": -4250
}
```

The string is for a person and a spreadsheet. The integer is for a program: written as a bare
JSON number it would become a floating-point value in most readers and stop being exact.

Not included: account digests, the per-book secret, cached payee vectors, running balances —
nothing secret and nothing derived. Only what was entered.

The full shape is in `specs/012-full-book-export/data-model.md`, and a test asserts the
document matches it.

## Encryption

| | |
|---|---|
| Key derivation | Argon2id, 4 iterations, 64 MiB, 2 lanes |
| Key | 256-bit, handed to SQLCipher as a raw key |
| Salt | In the sidecar, because it is needed before the database can be opened |
| Password | Stored nowhere, in any form |

## Amounts, everywhere

A `long` count of cents, stored as a SQLite `INTEGER`. No binary floating point is involved
anywhere. Rounding is half-away-from-zero, not .NET's banker's default, and a three-way split
of $10.00 gives $3.34 / $3.33 / $3.33 rather than quietly losing a cent.
