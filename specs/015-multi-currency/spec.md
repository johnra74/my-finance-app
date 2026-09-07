# Feature Specification: More than one currency

**Folder:** `015-multi-currency`
**Created:** 2026-09-05
**Status:** **Partly implemented** (2026-09-05) — the type and the totals are built; persistence is deferred, so a second currency still cannot be created. See `tasks.md`.
**Input:** Identified while retro-specifying the data model.

## Why this is a gap

`Account.CurrencyCode` exists, is an ISO 4217 code, and is **always `"USD"`**. Its own comment
says so: *"v1 is single-currency; this exists so amounts are labelled."*

`Money` carries **no currency at all** — it is a `long` count of minor units. So today,
`Money + Money` compiles and produces a number whatever two accounts those amounts came from.
Nothing prevents a second currency from being entered; nothing would make it wrong-looking;
and every total in the application would silently add unrelated numbers together.

That is why this needs a spec **before** an account in another currency is allowed in, not
after. The field is a promise the arithmetic does not keep.

## User Scenarios & Testing

### Primary user story

The user has an account in a second currency — a foreign bank account, a travel card — and
wants it in the same book, with its own register in its own currency, without corrupting any
total that spans both.

### Acceptance scenarios

1. **Given** accounts in two currencies, **When** each register is shown, **Then** each shows
   its own currency, correctly labelled.
2. **Given** accounts in two currencies, **When** the account list shows totals, **Then** it
   shows a subtotal **per currency** and no combined grand total — never a bare sum of unlike
   numbers.
3. **Given** a transfer between accounts in different currencies, **When** it is recorded,
   **Then** each leg carries the amount that actually moved in its own account's currency, as
   the two statements state them. The implied rate is a consequence, not an input.
4. **Given** a report spanning both, **When** it is run, **Then** it says what currency its
   figures are in.

### Edge cases

- **Rates do not arise**, because the application refuses to total unlike currencies and
  therefore never converts. This removes the whole class of failure a hand-entered rate brings
  with it — a stale figure silently misstating net worth.
- A book that has only ever held one currency must behave exactly as it does today, including
  showing a single grand total.
- Currencies whose minor unit is not 1/100 — JPY has none, and several have three decimal
  places. `Money`'s cents assumption does not survive this.
- Historical rates: a report over 2019 should arguably use 2019's rate, not today's.

## Requirements

### Functional requirements

- **FR-001**: An amount MUST carry its currency, or be unable to be added to an amount of
  another. Today neither is true.
- **FR-002**: The system MUST NOT produce a total that sums amounts in different currencies
  without conversion.
- **FR-003**: Each register MUST display its account's own currency.
- **FR-004**: A cross-currency transfer MUST record each leg in its own account's currency,
  taken from what each statement says actually moved. The two legs therefore do **not**
  cancel numerically, which is a change to the transfer invariant in
  `002-accounts-and-register` FR-026 and must be handled there explicitly rather than by
  loosening the check.
- **FR-005**: The system MUST support currencies whose minor unit is not 1/100.
- **FR-006**: The system MUST NOT hold exchange rates at all, and MUST NOT convert. Reports
  spanning more than one currency MUST present per-currency figures and MUST say which
  currency each figure is in.

### Non-functional requirements

- **NFR-001**: Conversion MUST remain exact in the sense of constitution 1 — no floating
  point, and a documented rounding rule at the point of conversion.
- **NFR-002**: The change MUST NOT alter any figure in an existing single-currency book.

## Key Entities

- **Currency** — code, minor-unit exponent, display format.
- **`Money`** — gains a currency, which is a change to the type every other feature is built
  on. This is where the cost of the feature actually is.

There is **no exchange-rate entity**. That is the point of the decision taken here.

## Success Criteria

- **SC-001**: Every existing test in the suite passes unchanged against a single-currency
  book.
- **SC-002**: Adding two amounts in different currencies is impossible, or fails loudly.
- **SC-003**: A book with two currencies produces per-currency subtotals and no combined
  grand total.
- **SC-004**: A book that has only ever held one currency looks and behaves exactly as it does
  today, grand total included.

## Assumptions

- Depends on constitution principle **1** — this is the principle the feature is most likely
  to damage, since it touches `Money` itself.
- This is a **breaking change to the foundation type**. It is not an additive feature, and its
  cost is mostly in the 836 existing tests and every arithmetic path, not in the new code.

## Out of Scope

- Fetching rates from any service. Constitution 7.
- **Converting between currencies at all**, and therefore any rate table, rate staleness
  policy or historical-rate question. Decided against here; revisit as its own spec if a
  single net-worth figure across currencies turns out to be worth the machinery.
- Multi-currency *categories* or budgets, until the account-level change has landed.

## Clarifications

### 2026-09-05

- **Q: With accounts in two currencies, what should a grand total do?** → **Refuse to total
  unlike currencies** (FR-002, FR-006). Per-currency subtotals; no conversion, and therefore
  no rate table, no staleness and no wrong number. It is also a far smaller change to `Money`
  and to the 836 existing tests than a book-currency design would be.
- **Q: Where do exchange rates come from?** → **Moot.** The application holds none.
- **Q: Must historical reports use the rate as at the transaction date?** → **Moot**, for the
  same reason.
- **Q: How is a cross-currency transfer recorded?** → Each leg in its own currency, as its own
  statement states it (FR-004). This deliberately breaks the numeric-cancellation half of the
  transfer invariant, which is called out rather than absorbed. *(Assumed as the consequence
  of refusing conversion, rather than asked.)*

## Where this stopped, and why

`Money` now carries its currency, derives its scale from it, and refuses to add or order two
currencies. Totals are reported per currency. The transfer invariant has been narrowed to
same-currency pairs. **All of that is live**, and every pre-existing test passes unmodified.

What is **not** built is persistence (`T005`, `T006`) and, deliberately behind it, the ability
to create an account in another currency (`T012`). So the feature is **inert**: a book still
cannot contain a second currency, and nothing observable has changed.

The spec assumed storing a currency beside each amount was a small change. It is not. EF Core
maps `Money` through a value converter to a single `INTEGER`, and a converter cannot carry two
columns into one struct; combining them needs either a materialization interceptor that
re-stamps every amount after load, or a schema-shaped rewrite that breaks the eight queries
currently translating `Money` comparisons and sums into SQL.

That is a schema fork with two defensible answers — a currency column per amount-bearing
entity, or a currency derived from the owning account — and a migration is expensive to
reverse, which is the whole reason `017-book-schema-upgrade` exists. It was left to be decided
by whoever actually needs a foreign account, rather than guessed at now for a capability
nobody is waiting on.

**Nothing is at risk in the meantime.** The staging in `plan.md` puts `T012` last precisely so
the feature cannot half-work: with it undone, a second currency cannot enter a book at all.

