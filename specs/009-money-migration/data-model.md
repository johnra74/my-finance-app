# Data Model: Bringing a Microsoft Money book across

Three layers: the **file format**, the **Money-level model** read out of it, and the
**mapping** into this application's schema.

## Layer 1 — the file

Jet 4, in the "MSISAM" flavour Money wrote.

| Element | Detail |
|---|---|
| Page size | 4096 bytes |
| Page types | 0 database definition · 1 data · 2 TDEF · 3/4 index · 5 usage bitmap |
| Encrypted pages | **1–14 always** (the system catalog), whatever the password state. Data pages too when a password is set — that case is refused. |
| TDEF offsets | definition length `0x08` · row count `0x10` · table type `0x28` · variable columns `0x2B` · column count `0x2D` · real index count `0x33` · index block `0x3F` |
| Column descriptor | 25 bytes |
| Index entry | 12 bytes |
| Row layout | 2-byte column count at the **front**; null mask, 2-byte variable count and *n+1* **reversed** offsets at the **back** |
| Text | compressed unicode, `FF FE` prefix, switching between 1 and 2 bytes per character mid-run |
| MEMO | 12-byte long-value header, stripped before decoding |
| Dates | OLE double, days from 1899-12-30, clamped to `[-657434, 2958465]` |
| Currency | int64 in ten-thousandths |

The catalog is never read. Tables are found by column signature:

| Table | Signature |
|---|---|
| Accounts | `hacct`, `szFull`, `at`, `amtOpen`, `fClosed` |
| Transactions | `htrn`, `hacct`, `dt`, `amt`, `grftt`, `lHpay` |
| Merchant codes | `sic`, `hcat` |

Two tables matching one signature ⇒ **null**, not a guess.

## Layer 2 — the Money model

Accounts, categories (three levels), payees, transactions with splits and transfer links, and
the merchant-code table. Read-only, and identical on every read of the same file.

## Layer 3 — the mapping

| Money | Here | Note |
|---|---|---|
| Account | `Account` | Type mapped; loan/investment become `UnsupportedImported`, balance-only. |
| Account number | — | **Not imported.** Money stores it encrypted; this application keeps no full numbers. |
| Category (3 levels) | `Category` (2 levels) + `CategoryKind` | Money's top level is only ever INCOME and EXPENSE. Nothing invented, nothing dropped. |
| Payee | `Payee` | Payees normalizing alike are folded; transactions follow the survivor. |
| Payee's usual category | `Payee.LastCategoryId`, `LastAmount` | **The reason a migrated book is immediately useful.** |
| Transaction | `Transaction` | Amount signed from the account's view — the same convention, so this is a translation, not a rebuild. |
| Split | `Split` | Uncategorized still gets one split, per `002-accounts-and-register`. |
| Transfer (two cancelling rows) | Two linked rows | Same shape. Legs point at each other. |
| Reconciliation state | `ClearedStatus` | Preserved. |
| Merchant code table | `MerchantCodeCategory` | Feeds the sixth source in `005-category-suggestion`. |
| Recurring bill definitions | — | **Counted and reported, not converted.** See `014-scheduled-bill-migration`. |
| Investment holdings | — | Not modelled. See `013-investment-accounts`. |

## Options and report

**Options:** keep closed accounts (default yes); keep projected-but-never-entered bills
(default yes — Money counts them in its balances, and the verification step depends on the
two matching).

**Report:** what was written stage by stage, and every account with its closing balance, laid
out to be read against Money's own account list.
