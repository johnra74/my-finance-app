# Implementation Plan: Categories and payees

**Spec:** `./spec.md` · **Status:** As-built

## Summary

Two small services over two small entity graphs, with the interesting work in two places:
the **two-level constraint**, enforced in the service because the schema cannot say it, and
**payee normalization**, a pure function in `MyFinance.Core` that both the register and the
importer depend on. The decision this turns on: **used categories are archived or merged,
never deleted**, so history filed correctly at the time stays correct.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 3 — writes through a service | `CategoryService`, `PayeeService`. The two-level rule and the merge semantics live there. |
| 4 — correctness outside WPF | `PayeeNormalizer` is pure and tested in `MyFinance.Core.Tests`. |
| 10 — the user's data stays theirs | Archiving preserves history rather than rewriting it. |

## Technical context

- **Projects:** `MyFinance.Core/{Entities,Payees}`, `MyFinance.Data/Services`,
  `MyFinance.App/ViewModels/Pages/{Categories,Payees}PageViewModel.cs`.
- **Testing:** `PayeeNormalizerTests` in Core; service behaviour against real books in
  `MyFinance.Data.Tests`.

## Design

### Two levels, and the flag that makes it enough

A category is a leaf name plus an optional parent, and `FullName` renders `Parent : Child`.
Income-versus-expense is a `CategoryKind` on the category rather than a third root level.
That is exactly Money's three-level tree with its two fixed roots (`INCOME`, `EXPENSE`)
turned into a flag — nothing has to be invented or dropped on migration, which is why the
constraint is affordable.

A child takes its parent's kind, and flipping a heading's kind takes the children with it.
Otherwise a book accumulates children that contradict their parents, and every report has to
decide which one it believes.

### Deleting versus archiving versus merging

Three operations, because they answer three different questions:

- **Delete** — only for a category never used. Anything else would have to destroy or re-file
  transactions that were correctly categorized at the time.
- **Archive** — hides it from pickers; history still resolves. The answer to "I stopped using
  this in 2014".
- **Merge** — moves every transaction and retires the source. The answer to "these two were
  always the same thing".

### Payee normalization, and the alias table

`PayeeNormalizer` upper-cases, strips punctuation and collapses whitespace, and **keeps
digits** — `SHELL 1234` and `SHELL 9876` may be different stations, and collapsing them
would be a decision the user never made.

The alias table is what makes a correction permanent. When two payees merge, the losing
normalized name is kept as an alias, so the next download carrying that descriptor lands on
the survivor rather than recreating the payee that was just merged away. Without it, tidying
the payee list would be undone by the next import.

### Alternatives rejected

- **Arbitrary category nesting.** More expressive, and every report then needs a rollup depth
  the user has to understand. Two levels is what the reference application taught this user.
- **A separate income/expense root category.** Would make the tree three levels for one bit
  of information, and every picker would show two nodes nobody ever files anything under.
- **Deleting a used category by re-filing its transactions to a fallback.** Silently changes
  the meaning of past reports.
- **Fuzzy payee matching at lookup time.** Non-deterministic and unexplainable. A normalized
  key plus an explicit alias table gives the same reach with a reason attached.

## Project structure

```
src/MyFinance.Core/Entities/{Category,Payee}.cs      Category, Payee, PayeeAlias
src/MyFinance.Core/Payees/PayeeNormalizer.cs         the normalization function
src/MyFinance.Data/Services/DefaultCategories.cs     the seeded chart
src/MyFinance.Data/Services/CategoryService.cs       two-level rule, archive, merge
src/MyFinance.Data/Services/PayeeService.cs          lookup, rename, merge, aliases
src/MyFinance.App/ViewModels/Pages/{Categories,Payees}PageViewModel.cs
```

## Risks

- **Changing normalization later re-partitions every payee in the book.** It is effectively
  part of the on-disk format. Any change needs a migration that re-normalizes existing rows,
  not just a new function.
- **Merge is not reversible.** The alias preserves the *name*, not the distinction. This is
  acceptable and stated in the UI, but it is the one destructive operation here.
- **The seeded chart is a guess about one user.** It is replaced wholesale on migration
  (`009-money-migration`), which is the case that matters most.
