# Categories, payees and suggestions

## The chart of categories

Two levels: a heading and its subcategories, the same shape Money used. Every category is
income or expense, and a child always agrees with its parent.

You can create, rename, re-parent, archive, delete and merge.

:::{note}
**A category that has been used cannot be deleted** — only archived or merged. Deleting one
would have to either destroy transactions or re-file them, and they were categorized
correctly at the time. Archiving hides it from the pickers and keeps the history readable.
:::

The category list is a **view**, not an editor: the Tax and Archived columns show what is
true and cannot be ticked there. Use **Edit** for tax-related, and the **Archive** button
for archiving.

## Payees

Add, rename, delete and merge. Merging keeps the losing name as an **alias**, so a future
import lands on the surviving payee rather than recreating the duplicate.

A payee cannot be deleted while it has transactions. Names are normalized — case,
punctuation and spacing collapse, digits are kept because they distinguish real shops — so
`WALMART #1234`, `Wal-Mart` and `wal mart` resolve to one payee.

## Categorization rules

Write a rule once — *"description contains SHELL → Transport : Fuel"* — and it decides every
future import.

Rules are tried **in order and the first match wins**, so a specific rule placed above a
general one overrides it. A rule can be tried against your existing register before you save
it, to check that it says what you meant.

## Where a suggestion comes from

Six sources, in descending order of authority. Each step down is one further from something
you actually decided.

| # | Source | Why it ranks there |
|---|---|---|
| 1 | **A rule you wrote** | A standing instruction in this application |
| 2 | **The category in the file** | A decision made elsewhere, at some point in the past |
| 3 | **What this payee was last filed under** | Your own history, exactly |
| 4 | **A classifier trained on your book** | Your history, generalised |
| 5 | **The merchant this one most resembles** | A guess, from a local model |
| 6 | **The merchant's industry code** | Where your bank sends one; many do not |

**Every guess is marked as a guess** in the preview, and none is applied without you seeing
it. On a new book with nothing to learn from, MyFinance says nothing at all rather than
inventing an answer.

In the transaction editor a suggestion only speaks when the category is still blank — so
opening a transaction to fix its amount will never argue with a decision you already made.

## Recognising a shop you have never used

The first four sources all need history. A genuinely new merchant matches none of them, and
on a freshly migrated book that is every new merchant.

That gap is filled by a **sentence-embedding model that runs on your machine**
(all-MiniLM-L6-v2, quantised to int8 and bundled into the executable). Every payee is turned
into a vector once and cached; an incoming descriptor becomes another, and the closest past
merchants vote. It can put `CONTOSO SUPERMARKET #543` next to `NORTHWIND GROCERY STORE` with no words in
common.

**What it is worth, measured rather than assumed.** Against a real book of 17,559
categorized transactions, trained on everything to November 2019 and tested on the six years
after:

| Source | Answers | Correct |
|---|---|---|
| This payee's remembered category | 62.6% | 92.7% |
| The classifier | 13.1% | 78.3% |
| *Neither — nothing to go on* | **24.3%** | — |
| Merchant resemblance, on that 24.3% | 16.4% of it | **62.9%** |

On a hundred-row statement it correctly fills about four rows that would otherwise arrive
blank, and gets about one and a half wrong. It is set conservatively on purpose: it is the
least reliable source and the only one that fires where you have no history, so it speaks
rarely and names what convinced it — *"Looks like NORTHWIND GROCERY STORE"*. A guess whose reasoning
you cannot see is one you have to check from scratch anyway.

Two things worth knowing:

- **Nothing leaves the machine.** The model runs locally; no transaction text is sent
  anywhere. It adds about 22 MB to the download.
- **It is entirely optional.** If the model or its native runtime will not load, the feature
  quietly disappears and the other five sources carry on exactly as before.
