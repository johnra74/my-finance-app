# Feature Specification: Investment accounts

**Folder:** `013-investment-accounts`
**Created:** 2026-09-05
**Status:** **Implemented** (2026-09-06), with one requirement it could not meet — see below.
**Input:** Identified while retro-specifying the codebase, as the largest functional gap
against the application this one replaces.

## Why this is a gap

`AccountType.UnsupportedImported = 99` is the shape of the hole. A brokerage, loan or
mortgage account brought across from a Money file arrives as a **balance-only** account: its
cash transactions are there, it is excluded from spending reports, and the register refuses to
write to it (`002-accounts-and-register`, FR-034). Securities, lots, prices and cost basis are
not modelled at all.

For a user whose Money file contains decades of holdings, the migration is honest about this —
it says what does not come across — but net worth is then incomplete, which is the one figure
on the dashboard that is supposed to be the whole picture.

## User Scenarios & Testing

### Primary user story

The user holds funds and shares in accounts they have tracked in Money for years. They want
those holdings to count toward net worth, to see what they own and what it cost, and to record
buys, sells and dividends — without this becoming a portfolio-management application.

### Acceptance scenarios

1. **Given** a brokerage account, **When** a buy is recorded, **Then** cash decreases and a
   holding is created or increased with its cost basis.
2. **Given** holdings and a price, **When** net worth is computed, **Then** the holdings'
   value is included.
3. **Given** a sale, **When** it is recorded, **Then** cash increases and the holding
   decreases.
4. **Given** a Money file with holdings, **When** it is migrated, **Then** they come across.

### Edge cases

- **A price nobody has updated for a year.** Prices are hand-entered, so this is the normal
  case rather than the exception: every value is shown **with the date of the price it came
  from**, so a stale figure is visible rather than silently wrong.
- **A holding with no price at all** — valued at cost, and said to be.
- A holding sold down to zero — the holding remains, at zero, so its history survives.
- A sale larger than the quantity held — refused.
- Splits, mergers and spin-offs — **out of scope for a first version**, and a holding that has
  undergone one will be wrong until the user corrects it by hand. Stated here because it is a
  real limitation, not a hypothetical.

## Requirements

### Functional requirements

- **FR-001**: The system MUST model a holding: a security, a quantity, and its cost.
- **FR-002**: The system MUST record buys, sells, dividends and cash movements against an
  investment account.
- **FR-003**: The system MUST include holdings in net worth.
- **FR-004**: The system MUST keep investment activity out of spending and income reports,
  the way transfers are kept out.
- **FR-005**: The system MUST migrate holdings from a Money file.
- **FR-006**: The system MUST track cost basis as an **average cost per holding**, not as
  individual lots. Sufficient for net worth and for what was paid; insufficient for capital
  gains, which this application does not report.
- **FR-007**: The system MUST record a price with the **date it applies to**, and MUST show
  that date wherever a valued figure appears, so a stale price is visible.
- **FR-008**: The system MUST value a holding with no price **at cost**, and say that it has
  done so.
- **FR-009**: The system MUST NOT support corporate actions — splits, mergers, spin-offs — in
  a first version, and MUST NOT silently misstate a holding that has undergone one. Where it
  cannot know, it says what it is showing.
- **FR-010**: OFX investment statement import is **out of scope**. The parser continues to
  refuse investment statements rather than partly reading them
  (`004-statement-import`, FR-007a).

### Non-functional requirements

- **NFR-001**: Quantities MUST be exact. `Money` is integer cents and cannot express a share
  count; a new exact quantity type is needed, and floating point is not acceptable
  (constitution 1).

## Key Entities

- **Security** — a name, a symbol, a type.
- **Holding** — an account, a security, a quantity, a cost basis, and possibly lots.
- **Investment transaction** — buy, sell, dividend, reinvestment, fee.
- **Price** — a security, a date, a value.

## Success Criteria

- **SC-001**: A migrated investment account's holdings and their cost reproduce what Money
  reports.
- **SC-002**: Net worth including holdings agrees with the sum of cash balances plus holding
  values.
- **SC-003**: Investment activity appears in no spending or income report.

## Assumptions

- Depends on constitution principles **1** (exactness — and it needs a new exact type),
  **7** (no network, which is what forces hand-entered prices), **2** (cash legs are ordinary
  signed transactions), **5** (a figure the user cannot see the basis of is not offered — hence
  the price date beside every value).
- The goal is *bookkeeping*, not portfolio analysis. No performance attribution, no
  benchmarking, no tax-lot optimisation.

## Out of Scope

- Live or delayed market data of any kind. Constitution 7.
- **Individual tax lots**, and therefore capital-gains reporting. See FR-006.
- **Corporate actions** — splits, mergers, spin-offs, stock dividends. See FR-009; a follow-on
  spec if holdings turn out to need it.
- **OFX investment statement import.** See FR-010; a follow-on spec.
- Performance measurement, asset allocation, rebalancing advice.
- Options, futures, or anything needing a position model beyond quantity and cost.

## Clarifications

### 2026-09-05

- **Q: Where do prices come from?** → **Hand-entered, with the price date shown beside every
  valued figure** (FR-007). Constitution 7 rules out fetching them; a file-import path is a
  whole import feature for a secondary capability. Showing the date is what stops a stale
  price being silently wrong.
- **Q: Lots, or average cost?** → **Average cost** (FR-006). Enough for net worth and for what
  was paid; wrong for capital gains, which this application does not report — and if it ever
  does, that spec brings lots with it.
- **Q: Are corporate actions in scope?** → **No, not in a first version** (FR-009). *(Assumed
  as the consequence of the average-cost decision rather than asked.)*
- **Q: Is OFX investment statement import in scope?** → **No** (FR-010). *(Assumed.)*

## What FR-005 could not deliver, and why

`SC-001` said a migrated holding's **cost** should reproduce what Money reports. It cannot,
and this is a fact about the file rather than a shortfall in the reader: Money's transaction
table carries **no quantity, price or cost column**, and its investment activity is not written
there in any form that could be replayed. In the reference book exactly one transaction carries
a security at all.

So a migrated holding brings across **what is owned and what it has been worth** — quantity,
and the price history Money recorded — and nothing about what it cost. The cost is left at zero
and the migration says so in as many words, rather than a zero being left to read as though the
shares had been free.

That is the same judgement `009` FR-024 made about due dates: better to report a gap than to
invent a figure that looks authoritative. Cost has to be entered by hand once, per holding.

