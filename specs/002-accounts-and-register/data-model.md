# Data Model: Accounts and the register

## Account

| Field | Notes |
|---|---|
| `Id` | |
| `Name` | Unique, non-blank. |
| `Type` | `AccountType`; frozen once transactions exist. |
| `Institution` | Display only. |
| `AccountNumberMasked` | Display only, e.g. `XXXXXXXX6464`. **Never the full number** — constitution 6. |
| `OpeningBalance` | `Money`. Counted in every balance. |
| `OpenedOn` | `DateOnly?` |
| `CurrencyCode` | ISO 4217. Always `USD` today; exists so amounts are labelled. See `015-multi-currency`. |
| `IsClosed` | Kept for history, dropped from pickers, sinks to the bottom of its group. |
| `IsFavorite` | Home dashboard tile. |
| `SortOrder` | Manual ordering within a group. |
| `LastReconciledOn`, `LastReconciledBalance` | Where the next reconciliation starts. |
| `LastUpdatedOn` | When a download was last imported. |
| `OfxAccountKey` | HMAC digest of the bank + account identifiers, under a per-book secret. See `004-statement-import`. |
| `OfxBankId` | Routing/issuer id. Identifies the institution, not the account. |
| `Notes` | Free text. |

`Group` is **derived** from `Type`, not stored: Checking, Savings, Money Market, CD and Cash
are Bank; Credit Card and Line of Credit are Credit; the unmodelled imported type is Other.
Deriving it means an account cannot be filed under a heading that contradicts its type.

### `AccountType`

`Checking`, `Savings`, `MoneyMarket`, `CertificateOfDeposit`, `Cash`, `CreditCard`,
`LineOfCredit`, and `UnsupportedImported = 99` — a loan, mortgage or investment account
carried over from a Money file. Balance-only: excluded from spending reports, refused as a
write target. The value is 99 rather than 8 so that real types can be added without
renumbering anything already on disk.

## Transaction

| Field | Notes |
|---|---|
| `Id`, `AccountId` | |
| `Date` | `DateOnly`. A transaction happens on a day, not at an instant. |
| `SequenceInDay` | The total order within a date. Without it the register reshuffles between sessions. |
| `Amount` | `Money`, signed from the owning account's view. Negative is money out. |
| `PayeeId` | Optional — a cash entry genuinely has no payee. |
| `Memo`, `Number` | |
| `ClearedStatus` | `Uncleared` / `Cleared` / `Reconciled`. Three states, three different balances. |
| `IsVoid` | Visible, counts toward nothing. |
| `TransferTransactionId` | The far leg. Null for an ordinary transaction. |
| `ImportBatchId` | Which import produced it, so an import can be undone. |
| Splits | At least one, always. |

## Split

`Id`, `TransactionId`, `CategoryId` (nullable — uncategorized is a real state), `Amount`,
`Memo`. **Splits always sum to the transaction amount**; this is the invariant
`TransactionValidator` exists to enforce, and `Money.Allocate` exists to make satisfiable.

## Derived, never stored

- **Running balance** — prefix sum over the ordered register.
- **Current / cleared / reconciled balance** — the same sum over three different subsets.
- **Account group** — from the type.
- **Uncategorized count** — a query over splits with no category, excluding transfers.

Each of these is a function of the rows. Persisting any of them would create a second source
of truth that could disagree with the first.
