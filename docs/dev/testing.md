# Testing

xUnit with Shouldly, four suites, all `net10.0` and all runnable on Linux.

```text
tests/MyFinance.Core.Tests        the domain: Money, balances, recurrence, projection,
                                  reports, budgets, chart geometry, holdings, diagnostics,
                                  threading
tests/MyFinance.Data.Tests        services against a real encrypted book, schema upgrades,
                                  migrations, and the interface-markup checks
tests/MyFinance.Import.Tests      OFX, QIF, payee cleanup, dedupe, sign analysis, and the
                                  Jet / MSISAM reader
tests/MyFinance.Semantics.Tests   the embedding model and the suggestion benchmark
```

```bash
./build.sh test
dotnet test tests/MyFinance.Core.Tests --filter "FullyQualifiedName~UiContext"
```

## How tests are named

A full sentence describing the behaviour, not the method under test:

```text
A_padded_cheque_number_loses_its_flag_and_padding
A_hand_typed_number_beginning_with_one_is_left_alone
Every_check_box_column_declares_whether_it_can_be_edited
The_repair_and_the_reader_agree
```

A specification's success criteria cite these names, so a criterion can be traced to the
test that proves it and a renamed test shows up as a dead citation.

## Nothing in this repository came out of a real book

Every name and every figure in the test suite is invented. Account names, payees, merchants
and balances are made up — the sample-database cast (Contoso, Fabrikam, Northwind, Adventure
Works, Woodgrove) precisely because nobody banks with them.

That is a rule, not a habit. A test that asserts a real balance publishes it, and a test that
names a real account publishes which bank someone uses. Both are permanent once committed.

### The invented book

`SampleBook` (in `MyFinance.Data.Tests/Fixtures`) builds a whole `MoneyBook` — five accounts,
a chart of categories, eleven payees, four years of transactions with splits, transfers,
recurring bills and holdings — from a fixed seed. `SampleBookMigrationTests` runs the
end-to-end migration invariants over it: every balance survives, every transaction's splits
add up, both legs of every transfer point at each other, only verified recurrences convert.

Those checks used to run only where a real `.mny` happened to be sitting, which meant the
ones that mattered most were the ones most often skipped.

## Tests that need a real Money file

`MoneyFileFactAttribute` skips when no `.mny` is present, and runs when one is in the
repository root. What is left for them is the one thing an invented book cannot do: meet the
shapes nobody thought to invent.

:::{danger}
**Principle 8.** Those tests assert only what stays true of *any* book — a row count, a
shape, an invariant. **Never a balance, an account name or a payee.** `.gitignore` excludes
every financial file format, and the only exceptions are the redacted fixture folders
`tests/fixtures/ofx/redacted/` and `tests/fixtures/qif/redacted/`.
:::

## Testing the interface without Windows

No test project references `MyFinance.App` — it targets `net10.0-windows`, and the build runs
on Linux. Two things are checked anyway:

- **Its markup, as XML.** `MyFinance.Data.Tests` reads every `src/MyFinance.App/Views/**/*.xaml`
  and fails any `DataGridCheckBoxColumn` that does not declare `IsReadOnly` or an explicit
  `Mode`. That closes the whole class of "binds TwoWay by default onto a read model", which
  produced two live faults.
- **The mechanism, in Core.** The cross-thread crash is reproduced without WPF: work
  finishing on a pool thread, awaited with no ambient context, asserted to resume on the
  captured one.

Anything genuinely needing a person — the printed output, the three exception hooks actually
firing — is marked ⚠️ in the relevant specification's coverage table rather than pretended
to be covered.

## The schema interlock

`SchemaUpgradeTests.The_current_schema_version_matches_the_migration_count` asserts
`BookSchema.Current` equals the number of migrations in the assembly. Adding a migration
without bumping the constant **fails the build** rather than shipping a book that misreports
itself. See {doc}`schema-migrations`.
