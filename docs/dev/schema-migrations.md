# Schema migrations

The book is often the only copy of the user's records, it is encrypted so nobody can inspect
it, and there is no password recovery. That sets the risk budget for everything on this page.

## Adding a migration

On Windows the build script wraps `dotnet-ef` and installs it on first use:

```text
build ef                                    :: list migrations
build ef migrations add AddSomething --project src\MyFinance.Data --output-dir Migrations
```

Anywhere else, install the tool yourself:

```bash
dotnet tool install --global dotnet-ef --version 10.0.0
dotnet ef migrations add <Name> --project src/MyFinance.Data --output-dir Migrations
```

`DesignTimeDbContextFactory` points at a throwaway unencrypted path, because scaffolding only
needs the model shape.

:::{important}
**Then bump `BookSchema.Current`.** It is one integer per migration, in order. A test asserts
it against the number of migrations in the assembly, so forgetting fails the build rather
than shipping a book that misreports its own version.
:::

## Why a version at all

EF Core's migrations history answers *"which migrations have run here"*, not *"was this file
written by a build newer than me"*.

EF reads the columns it recognises and ignores those it does not — so an older build opening
a newer book would find nothing wrong, and would then write rows shaped for a schema it has
never seen, into the one file with no recovery path. A single integer, compared before
anything is read, is what makes that detectable.

The version is written to **both** the database and the plaintext sidecar. The sidecar copy
is what allows it to be checked *before* the database is opened.

| Comparison | What happens |
|---|---|
| Book **older** than this build | Upgraded, after a verified backup |
| Book **matches** | Opened |
| Book **newer** | **Refused**, with an explanation |

A book carrying no version at all predates versioning and is read as the schema current when
stamping shipped. Treating absence as unknown would have made the feature's first act be to
refuse every book already on disk.

## How an upgrade is made safe

1. The password is proved and the recorded version read, **before anything is written
   anywhere**.
2. A backup is taken — through the same service that **reads every archive back before
   accepting it**. If it cannot be written, the upgrade refuses to proceed and the book is
   untouched.
3. The migrations are applied to a **copy**, which replaces the original only once every one
   of them has succeeded. Stale `-wal` and `-shm` files are removed, because they belong to
   the pre-upgrade database and would corrupt the new one.
4. The sidecar is advanced **last**.

:::{note}
Copy-and-replace rather than a transaction, because **EF Core wraps each individual migration
but not the sequence of them**. Upgrading in place could leave a book three migrations into a
five-migration run — a state nothing knows how to read and nobody can inspect, because the
file is encrypted.
:::

## Data repairs

Schema 5 was the first migration that changes no table: it decodes Microsoft Money's
cheque-number sort key in books migrated before the reader knew about it.

Rewriting a user's records in place is a heavier act than adding a column — a wrong rule
corrupts values with no history to recover them from — and it is acceptable only because the
upgrade path already requires a verified backup and refuses without one.

Two rules for a data repair:

- **Keep the SQL in one place and test that text.** The repair statement lives as a constant
  the migration and the test both execute, so the two cannot drift.
- **Test it against the C# that does the same job.** `MoneyNumberRepairTests` runs the SQL and
  `MoneyNumber.Decode` over one shared list of cases and asserts they agree. Stating a rule
  twice is the risk; that test is what makes it manageable.

And be narrow. The repair's guard against a hand-typed cheque number beginning with `1` is
the load-bearing part of it, not a nicety.

## History

| Version | Migration | |
|---|---|---|
| 1 | `InitialSchema` | |
| 2 | `AccountOfxLink` | Account matching for imports |
| 3 | `SuggestionSources` | Payee vectors and merchant codes |
| 4 | `Investments` | Securities, holdings, activity, prices |
| 5 | `DecodeMoneyNumberField` | Data repair — no shape change |
