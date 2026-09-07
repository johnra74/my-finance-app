# Data Model: More than one currency

One new value type, one changed foundation type, one migration — and, deliberately, **no
exchange-rate entity**.

## Currency (new)

| Field | Notes |
|---|---|
| `Code` | ISO 4217, e.g. `USD`, `GBP`, `JPY`. |
| `MinorUnitExponent` | 2 for most, **0 for JPY**, 3 for several. This is the field `Money.MinorUnitsPerUnit = 100` currently hard-codes and gets wrong. |
| Display format | Symbol and placement, for rendering only. |

`Currency.Default` exists so every current call site keeps compiling and every existing amount
keeps meaning what it means.

## Money (changed)

Today:

```csharp
public readonly record struct Money { public long MinorUnits { get; } }
public const int MinorUnitsPerUnit = 100;
```

After:

- Carries its `Currency`.
- Derives its scale from the currency's exponent rather than a constant, in `FromDecimal`,
  `ToDecimal`, `FromUnits`, formatting and parsing.
- **`+`, `-` and comparison across two currencies throw.** Not coerce, not return zero, not
  take the left operand — this is a programming error the aggregation layer is supposed to
  make unreachable, and a silent answer here is the same class of bug as the one this spec
  exists to close.
- `Money.Zero` is currency-agnostic and absorbs into any currency, so summing an empty
  sequence still works.

### Persistence

Minor units stay a SQLite `INTEGER` — constitution 1 is untouched. The currency code is stored
**beside** it, on each row that carries an amount. Storing it only on the account would be a
smaller change and would reintroduce the exact problem: an amount whose currency the
arithmetic cannot see.

### Migration

One migration, defaulting every existing row to the book's single currency. Existing books are
single-currency by definition, so this is exact rather than a best guess.

## Account (unchanged in shape)

`CurrencyCode` already exists and is already ISO 4217. After this feature it stops being
decorative and becomes the currency every amount in that register is denominated in.

## Transfers, and the invariant that changes

`002-accounts-and-register` FR-026: a transfer is two rows that **cancel**.

Across currencies they cannot: $100 leaves one account and £78 arrives in the other, exactly
as the two statements state. So:

- **Same currency** — the legs must still cancel exactly. This assertion is unchanged and must
  stay as strong as it is today.
- **Different currencies** — the legs must still name each other and share a date; the numeric
  cancellation is not checked, because there is no rate with which to check it.

No rate is stored, even implicitly. The pair of amounts is a record of what actually moved,
which is what the statements say and what the registers must show.

## Derived, per currency rather than per book

Account-list subtotals, the grand total, report totals, budget figures and net worth all
become **per-currency groups**. For a book that has only ever held one currency this is a
single group, rendered exactly as today — no extra heading and no currency label appearing on
a book that never needed one.

## Deliberately absent

**No exchange-rate entity, no rate table, no rate date, no conversion function.** This is the
decision, not an omission: with no conversion there is no staleness, no
historical-versus-current policy, no rounding at a conversion boundary, and no figure that is
quietly wrong because a rate is six months old.
