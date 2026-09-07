# The eleven principles

Every specification, plan and change in this repository is checked against these. They live in
`.specify/memory/constitution.md` — that file is the authority and this page is the guided
tour.

Each is a decision that was already made and is already visible in the code, and each is
written with the consequence that makes it non-negotiable. **A principle without a stated
cost is one that gets traded away the first time it is inconvenient.**

:::{list-table}
:header-rows: 1
:widths: 4 32 64

* - #
  - Principle
  - Why it cannot be traded away
* - 1
  - **Money is an integer count of cents**
  - A cent lost to a `double` does not announce itself. Rounding is half-away-from-zero, not
    .NET's banker's default, and `Money.Allocate` splits a total into shares that sum back
    exactly.
* - 2
  - **Amounts are signed from the owning account's point of view**
  - Negative is money out. A transfer is two linked rows. The alternative — one row with two
    account references — makes every balance query a special case.
* - 3
  - **Every write goes through a service in `MyFinance.Data/Services`**
  - An invariant enforced in only some write paths is not an invariant, and transactions
    arrive from the register, from file import and from the bills engine.
* - 4
  - **Correctness lives outside the WPF layer**
  - The UI is the one layer this project cannot prove. Everything that must be provably
    right goes somewhere it can be tested without Windows.
* - 5
  - **A guess is never applied unseen**
  - A wrong category you notice is a nuisance; one applied silently is a corrupted book you
    will trust. Every suggestion is marked, and says what convinced it.
* - 6
  - **Store nothing you do not need**
  - Data you do not hold cannot leak. No full account numbers, no passwords, no credentials.
* - 7
  - **Nothing leaves the machine**
  - One person's complete financial history, on their machine. No telemetry, no crash
    reporting, no update check, no price feed, no network calls at all.
* - 8
  - **Real financial data never enters source control**
  - Git history is permanent, and a routine `git add -A` is all it takes. Committed tests
    assert structural invariants only — never a balance, an account name or a payee. Nor is
    real data admitted transcribed: every name and figure in the tests, the specifications
    and these pages is invented, and `SampleBook` is the invented book to build on.
* - 9
  - **Long work is off the UI thread, cancellable, and transactional**
  - An application that stops repainting is indistinguishable from a crashed one, and a
    half-finished import is worse than no import. Coming back is **explicit**:
    `ConfigureAwait(true)` is not a guarantee, so work marshals through `UiContext`.
* - 10
  - **The user's data stays theirs**
  - Money's file format is the reason this application exists. Nothing produced here may be
    readable only from inside it. Every report exports to CSV and the whole book exports as
    one documented JSON file.
* - 11
  - **Foreign files are opened read-only**
  - A `.mny` file may be the only copy of twenty-five years of records. It is never opened
    for writing, moved or changed.
:::

## How they are used

They are not decoration. Each specification names, under **Assumptions**, the principles it
depends on, and a plan carries a constitution-check table filled in honestly. Two of them
have caught real mistakes during ordinary work:

- **Principle 8** caught a real account name and balance in one plan, and real payee names in
  another. Both were scrubbed before they reached a file that would be committed. A later
  sweep found the same thing had happened by a slower route — real account names and balances
  transcribed into test fixtures and doc comments over months — which is why the principle now
  says so explicitly and why there is an invented book to reach for instead.
- **Principle 10** was found to be only *half* kept — reports exported, the book did not —
  and that gap was invisible until the specifications were written down and checked against
  each other. It is kept now.

## Amending one

The constitution carries a version and an amendment date. Changing a principle means
changing the specifications that depend on it, so the amendment is the small part.
