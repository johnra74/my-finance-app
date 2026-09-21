# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Quick Start

**Building and Testing (Windows):**
```bash
.\build.ps1          # build + test (default)
.\build.ps1 run      # launch the app
.\build.ps1 publish  # create self-contained .exe in artifacts\publish
```

**Building and Testing (Linux/macOS):**
```bash
./build.sh          # build + test
./build.sh publish  # cross-build Windows .exe
```

**Single test project:**
```bash
dotnet test tests/MyFinance.Core.Tests
dotnet test tests/MyFinance.Data.Tests
dotnet test tests/MyFinance.Import.Tests
dotnet test tests/MyFinance.Semantics.Tests
```

**Documentation (Sphinx):**
```bash
python3 -m venv .venv-docs
.venv-docs/bin/pip install -r docs/requirements.txt
.venv-docs/bin/sphinx-build -W -b html docs docs/_build/html
```

**Database migrations (EF Core + SQLCipher):**
```bash
.\build.ps1 ef migrations list --project src\MyFinance.Data
.\build.ps1 ef migrations add MigrationName --project src\MyFinance.Data --output-dir Migrations
.\build.ps1 ef database update --project src\MyFinance.Data
```

## Architecture Overview

MyFinance is a multi-layered desktop personal finance application. Key structural principle: **only `MyFinance.App` targets Windows (WPF); all other projects are platform-neutral, testable everywhere.**

### Project Structure

| Project | Purpose | Framework |
|---------|---------|-----------|
| **Core** | Domain model, Money struct, register/balance logic, recurrence, cash-flow projection, report engine, budget arithmetic, chart geometry, diagnostics, threading | net10.0 |
| **Data** | EF Core database abstraction layer, SQLCipher schema, migrations, data access services | net10.0 |
| **Import** | OFX/QIF parsing, duplicate detection, sign analysis, payee cleanup, categorization rules, ML suggestion (Jet 4/MSISAM reader for .mny files from Microsoft Money) | net10.0 |
| **Semantics** | Isolated embedding model for category suggestion; separated to keep other projects pure and quick to test | net10.0 |
| **App** | WPF shell, views, view models, UI orchestration | net10.0-windows |

### Layering Pattern

The Core → Data → App dependency chain is intentional. **UI logic never bleeds into Core or Data.** Business logic does not instantiate databases or load models directly. Abstractions (interfaces in Core) prevent upward dependencies.

### Testing Strategy

- **Test projects:** Use `xUnit` + `Shouldly` assertion library
- **Test organization:** Core, Data, Import, and Semantics each have a corresponding test project
- **Real data:** Tests use redacted fixtures in `tests/fixtures/ofx/redacted/` and `tests/fixtures/qif/redacted/`. Real financial data never enters source control.
- **Database tests:** Hit a real SQLCipher database when needed (not mocked)
- **Fixtures:** `.mny` (Microsoft Money) file tests skip gracefully when the file is absent; they assert only structural invariants (never specific balances or names)

### Key Dependencies & Integrations

- **Database:** EF Core 10 + SQLCipher (encrypted at-rest)
- **WPF:** Only in `MyFinance.App`; no WPF references in Core/Data/Import/Semantics
- **Parsing:** OFX 1.x & 2.x, QIF, `.qfx`, `.qbo` via custom parsers
- **ML/Semantics:** Local embedding model (weights downloaded by `tools/fetch-model.sh` on Windows, `tools\fetch-model` on Unix)
- **Documentation:** Sphinx + MyST (Markdown source, read-the-docs hosted)

## Code Patterns & Conventions

### Configuration & Build

- **Solution file:** `MyFinance.slnx` (VS format)
- **Project defaults:** `Directory.Build.props` sets language version to latest, enables nullable reference types, implicit usings, treats warnings as errors
- **No telemetry:** `DOTNET_CLI_TELEMETRY_OPTOUT=1` in build scripts
- **Security:** Book is encrypted with a password-derived key (key never stored); `.mfdb` + `.mfmeta` sidecar files both required to open a book

### Naming & Code Style

- Implicit usings enabled → no `using System` boilerplate
- Nullable reference types enabled → `string?` vs `string` is enforced
- Latest C# language features are in use
- TreatWarningsAsErrors is true → code must compile cleanly

### Cross-Platform Considerations

- `EnableWindowsTargeting=true` allows the whole solution to compile on Linux/macOS (WPF projects compile but cannot run)
- Core/Data/Import/Semantics are net10.0 and run anywhere
- App is net10.0-windows and only runs on Windows
- Build scripts handle both platforms: `build.sh` for Unix, `build.ps1` for Windows

### Documentation as Code

- Specs in `specs/` follow GitHub Spec-kit conventions
- `.specify/memory/constitution.md` documents eleven guiding principles that all specs are checked against
- **Principle:** "Where the code and a specification disagree, the code is the fact and the specification is the bug."
- Full developer docs in `docs/` (architecture, building, testing, schema migrations, principles, licensing)

## Important Notes for Contributors

1. **No password recovery.** The book is encrypted with a password-derived key that is never stored anywhere. Forgetting the password destroys the data.
2. **Two-file book format.** Both `.mfdb` and `.mfmeta` are required; losing either is fatal.
3. **No network calls.** Nothing leaves the machine—no telemetry, crash reporting, update checks.
4. **Real financial data never in source control.** Only redacted fixtures in `tests/fixtures/`.
5. **Embedding model weights** are fetched separately (not committed) by `tools/fetch-model.sh` or `tools\fetch-model`.
6. **Publish process** creates a self-contained single-file .exe with embedded documentation and licenses.

## References

- **User docs:** `docs/` (build with Sphinx)
- **Architecture:** `docs/dev/architecture.md`
- **Building:** `docs/dev/building.md`
- **Testing:** `docs/dev/testing.md`
- **Schema & migrations:** `docs/dev/schema-migrations.md`
- **Specifications:** `docs/dev/specifications.md` and `specs/`
- **Principles:** `docs/dev/principles.md` (the eleven principles)
