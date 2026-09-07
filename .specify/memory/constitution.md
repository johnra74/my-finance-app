# MyFinance Constitution

**Version:** 1.0.0 · **Ratified:** 2026-09-05 · **Last amended:** 2026-09-05

These are the rules every spec, plan and change in this repository is checked against.
Each one is a decision that was already made and is already visible in the code; each is
written with the consequence that makes it non-negotiable, because a principle without a
stated cost is one that gets traded away the first time it is inconvenient.

---

## Principle 1 — Money is an integer count of cents

Every amount is a `Money` (`src/MyFinance.Core/Primitives/Money.cs`): a `readonly record
struct` over a `long` count of minor units, stored as a SQLite `INTEGER`. No binary
floating point participates in any monetary calculation anywhere in the solution.
Rounding is half-away-from-zero, not .NET's banker's default. `Money.Allocate` splits a
total into shares that sum back to exactly the original.

**Why it cannot be traded away:** a cent lost to a `double` does not announce itself. It
shows up as a reconciliation that will not close, months later, with no way to find which
transaction was wrong.

## Principle 2 — Amounts are signed from the owning account's point of view

Negative is money leaving that account. A credit-card balance is therefore negative while
money is owed, matching Microsoft Money. A transfer is **two** linked rows, one in each
account, so every register balances on its own and reports can exclude transfers wholesale
rather than special-casing them.

**Why:** the alternative — one row with two account references — makes every balance query
a special case, and the first query that forgets the special case is silently wrong.

## Principle 3 — Every write goes through a service in `MyFinance.Data/Services`

Never through a `DbContext` obtained elsewhere. Transactions arrive from the register, from
file import, from the bills engine and from migration; the invariants below cannot be
expressed in the schema.

**Why:** an invariant enforced in only some write paths is not an invariant.

## Principle 4 — Correctness lives outside the WPF layer

Only `MyFinance.App` targets `net10.0-windows`. Balance arithmetic, recurrence, forecasting,
report aggregation, budget maths and chart geometry live in `MyFinance.Core` and are tested
where no Windows machine is required. A bar whose length disagrees with its label is a lie
the eye cannot catch, so that arithmetic is not left to the layer that can only be verified
by looking at it.

**Why:** the UI is the one layer this project cannot prove. Everything that must be provably
right therefore lives somewhere else.

## Principle 5 — A guess is never applied unseen

`CategorySuggestion.IsCertain` draws the line. A rule the user wrote, a category the file
carried, and this payee's usual category may fill a field. The statistical classifier and
the merchant-resemblance model may only *offer*, with their reasoning named.

**Why:** a wrong category the user notices is a nuisance. A wrong category applied silently
to two hundred rows is a corrupted year of reports.

## Principle 6 — Store nothing you do not need

The password is never stored, in any form — there is no recovery path, deliberately. Full
account numbers are never stored: only a display mask, plus an HMAC-SHA256 digest under a
per-book secret where a statement has to be matched to an account. An HMAC and not a plain
hash, because account numbers carry far too little entropy to hash safely.

**Why:** data you do not hold cannot leak.

## Principle 7 — Nothing leaves the machine

No network calls, no telemetry, no crash reporting, no account aggregation service. Bank
data arrives only as a file the user downloaded themselves. The embedding model runs
locally. OFX Direct Connect was considered and rejected, so no bank credential is ever
collected or stored.

**Why:** this is a single-user application holding one person's complete financial history.
The only guarantee that survives every future bug is that there is nowhere for it to go.

## Principle 8 — Real financial data never enters source control

`.gitignore` excludes `*.mfdb`, `*.mfmeta`, `*.mfbak`, `*.mny`, `*.ofx`, `*.qfx`, `*.qbo`
and `*.qif` everywhere, with one narrow exception: files under
`tests/fixtures/ofx/redacted/` and `tests/fixtures/qif/redacted/`. Tests that run against a
real book assert structural invariants only — never a balance, an account name or a payee.

**Nor does real data enter source control transcribed.** Every name and figure in the tests,
the specifications and the documentation is invented; the sample-database cast (Contoso,
Fabrikam, Northwind, Adventure Works, Woodgrove) is used precisely because nobody banks with
them. Copying a balance out of a real register into a test, or a real account name into a doc
comment as an example, publishes it just as permanently as committing the file would.
`SampleBook` in `tests/MyFinance.Data.Tests/Fixtures` is the invented book to build on.

**Why:** git history is permanent. A routine `git add -A` is all it takes.

## Principle 9 — Long work is off the UI thread, cancellable, and transactional

Anything that can take more than a moment reports progress through
`MyFinance.Core.Progress.WorkProgress`, runs on a pool thread, and can be stopped. Stopping
leaves the book exactly as it was, because the writes run inside a database transaction.

Coming back is explicit. `ConfigureAwait(true)` resumes on whatever synchronization context
happened to be current at the await, and on none it resumes wherever the work finished — so
it is not a guarantee, and from a fire-and-forget call site it is frequently wrong. Work that
returns to the interface marshals through `MyFinance.Core.Threading.UiContext`.

**Why:** an application that stops repainting is indistinguishable from one that has
crashed, and a half-finished import is worse than no import. The resumption rule is here
because the alternative failed silently for months: a page rebuilt its bound collection on a
pool thread, WPF threw, nobody had awaited the task, and the only symptom was a screen that
did not refresh.

## Principle 10 — The user's data stays theirs

Microsoft Money's file format is the reason this application exists. Nothing produced here
may be readable only from inside it.

**Status: kept**, as of 2026-09-05. Any report exports to CSV, and the whole book exports as
one documented JSON file — see `specs/012-full-book-export`. This principle spent the life of
the project only half kept; it is worth remembering that the gap was invisible until the specs
were written down and checked against each other.

## Principle 11 — Foreign files are opened read-only

A `.mny` is opened for reading, and is never written, moved, renamed or modified. Migration
writes into an empty book only.

**Why:** the file being read may be the only copy of twenty-five years of records, and a
failed migration must not be able to damage it.

---

## Governance

- Amendments require a version bump here and a note of which specs are affected.
- Semantic versioning: MAJOR removes or reverses a principle, MINOR adds one, PATCH is
  wording.
- Every `spec.md` names in its **Assumptions** section the principles it depends on. A
  principle no spec depends on is one nobody will keep, and should be questioned rather
  than quietly retained.
- Where the code and a spec disagree, the code is the fact and the spec is the bug.
