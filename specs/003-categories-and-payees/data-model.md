# Data Model: Categories and payees

## Category

| Field | Notes |
|---|---|
| `Id` | |
| `Name` | **Leaf name only** — `"Mobile phone"`, not `"Bills : Mobile phone"`. |
| `ParentId` / `Parent` / `Children` | One level of nesting. A child may not have children. |
| `Kind` | `Expense` or `Income`. A child always matches its parent. |
| `IsTaxRelated` | Feeds the tax-related transactions report. |
| `IsArchived` | Hidden from pickers, still resolves for history. |
| `SortOrder` | |
| `FullName` | **Derived**: `Parent is null ? Name : $"{Parent.Name} : {Name}"`. Never stored — a stored path would go stale the moment a heading is renamed. |

**Uniqueness:** the leaf name is unique *within a parent*. `Bills : Insurance` and
`Auto : Insurance` are two different categories and both are legitimate.

## Payee

| Field | Notes |
|---|---|
| `Id` | |
| `Name` | Display name, e.g. `"Woodgrove Mortgage"`. |
| `NormalizedName` | Upper-cased, punctuation-stripped, whitespace-collapsed. The match key. |
| `LastCategoryId` | What this payee was last filed under. The third source in the suggestion chain (`005`). |
| `LastAmount` | Offered as a default in the register. |
| `IsActive` | |
| `Notes` | |
| `Aliases` | Other normalized descriptors that resolve here. |

## PayeeAlias

`Id`, `PayeeId`, `NormalizedPattern`.

Written when payees are merged, and when the user corrects a cleaned-up import descriptor.
It is the reason a correction made once survives every later download — see
`004-statement-import`.

## Normalization contract

Deterministic, and effectively part of the on-disk format:

1. Upper-case.
2. Strip punctuation.
3. Collapse whitespace runs to a single space, and trim.
4. **Keep digits** — they distinguish real payees.
5. A name normalizing to the empty string is refused, rather than creating a key that
   everything matches.

## The seeded chart

`DefaultCategories.Seed` runs once, inside the same failure envelope as the schema, so a new
book is never left with no way to classify anything. It is replaced wholesale by a `.mny`
migration (`009-money-migration`) — a starter chart is a guess, and the user's own chart is
not.
