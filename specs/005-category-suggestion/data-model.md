# Data Model: Recommending a category

## Persisted

### CategorizationRule

| Field | Notes |
|---|---|
| `Id`, `Name` | A nameless rule is refused — an unexplainable rule is worse than none. |
| `Priority` | The evaluation order. First match wins. Ties break on age, deterministically. |
| `IsEnabled` | Disabled rules are kept and left out of the order. |
| `AccountId?` | Optional restriction to one account. |
| Conditions | Payee, description or amount; a comparison kind; case sensitivity; combinable. Amount comparisons run against the **signed** amount, so "money out" reads as negative. |
| `CategoryId?`, `PayeeId?` | What it does. A rule doing neither is refused. |
| Usage count | Shown on the rules screen so a rule that never fires is visible. |

### PayeeEmbedding

`PayeeId`, `TextHash`, `Model`, and the int8-quantised vector. The three-part key means a
changed payee name or a changed model invalidates the cache without anyone clearing it.

### MerchantCodeCategory

An industry (SIC) code to a category. Brought across from Money's own curated table — see
`009-money-migration`. Many banks send no code at all, so this is a bonus source rather than
a dependable one.

### Read, not owned, by this feature

- `Payee.LastCategoryId` — the third source. Written by `002-accounts-and-register`.
- Transactions and their splits — the classifier's training set. Transfers and voided rows
  are excluded: a transfer is not spending, and a voided row is not a decision.

## In memory

### CategorySuggestion

`CategoryId?`, `Source`, `Confidence` (0–1), `PayeeId?` (when a rule rewrites the payee),
`Rule?`, `SimilarTo?`, `IsCertain`, `Describe()`.

### SuggestionSource

Ordered by descending authority, and the numbering is the specification:

| Value | Certain? | Measured accuracy |
|---|---|---|
| `Rule = 1` | ✅ | by construction |
| `FileCategory = 2` | ✅ | — |
| `PayeeMemory = 3` | ✅ | 92.7% |
| `Statistical = 4` | ✗ offer | 78.3% |
| `Similar = 5` | ✗ offer | 62.9% |
| `Sic = 6` | ✗ offer | — |

`None = 0` is not a certainty either — an absent answer is not an authoritative one.

### SuggestionContext

`Rules`, `Classifier`, `Similar`, `MerchantCodes`. Built once, reused, rebuilt when the
fingerprint changes.

### BookFingerprint

`Transactions`, `Rules`, `Payees`, `NewestTransactionId`, `NewestSplitId`.

The split id is the load-bearing member: recategorizing a transaction changes no count and no
transaction id, and would otherwise leave the classifier trained on the mistake it was just
corrected from.
