# Data Model: Importing a bank statement

Two vocabularies: what a *file* yields (pure, in `MyFinance.Import`, never persisted as-is),
and what is *written* (in the book).

## From the file — `MyFinance.Import/Model`

### ImportedStatement

Account identifiers as the file states them (bank id, account id, account type), currency,
the statement date range, the **stated ending balance** and its date, the rows, and every
diagnostic raised while reading. The stated balance is not decoration: it is the evidence the
sign check runs on.

### ImportedTransaction

| Field | Notes |
|---|---|
| Date | As written. The time zone is parsed and **not applied** — see FR-008. |
| Amount | As written, before any sign decision. |
| Reference | The bank's own id (`FITID`). Authoritative for duplicate detection. |
| Descriptor | The raw text, always preserved into the memo. |
| Type | What the bank called it. Weak evidence for the sign check. |
| Merchant code | An industry code where the bank sends one. Feeds `005-category-suggestion`. |
| Cleared flag | Optional; absent leaves the state unstated. |
| Number, category, splits | From QIF, which carries what OFX does not. |

### ImportDiagnostic

Severity, message, and the row it concerns. A file is read to the end and its problems
reported; nothing is silently dropped.

## Into the book

### ImportBatch

`Id`, when it ran, the source file name, the account, the row count, and the transactions it
wrote. The unit of undo.

- Undo removes exactly the batch's rows.
- Refused once any of them is reconciled, and refused a second time.
- **Payees and payee aliases created by the batch are kept.** Those were learned; the
  transactions were the mistake.

### Written onto Account

- `OfxAccountKey` — HMAC-SHA256 of the bank and account identifiers under a per-book secret
  (`AppSetting["import.ofx.account_key_secret"]`). **The account number itself is never
  stored.** An HMAC and not a plain hash: account numbers have too little entropy to hash
  safely, and an enumerable digest would defeat the purpose.
- `OfxBankId` — the routing or issuer id. Identifies the institution, not the account, and is
  kept so the user can be shown *why* a file matched.
- `LastUpdatedOn`.

### Written onto Transaction

- `ImportBatchId` — which import produced it.
- The bank's reference — kept, because it is what makes the next import's duplicate detection
  exact.
- `ClearedStatus = Cleared` — an imported row has reached the bank by definition.
- `SequenceInDay` — distinct and increasing for rows sharing a date, so the register has a
  total order (`002-accounts-and-register`).
- Memo — carries the raw descriptor.

### Written onto Payee

A `PayeeAlias` per corrected descriptor, keyed on the **stable** part of the descriptor, so
the correction survives next month's file. See `003-categories-and-payees`.
