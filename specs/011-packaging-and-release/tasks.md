# Tasks: Packaging and release

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

This feature owns no entities, so there is no `data-model.md`.

## Phase 1 — Solution-wide settings

- [X] **T001** Central version, product metadata, warnings as errors, real globalization data
  - Implements: `Directory.Build.props`, `Directory.Packages.props`
  - Proven by: the solution builds with `TreatWarningsAsErrors=true` and zero warnings
  - Requirements: FR-007, FR-016
- [X] **T002** `EnableWindowsTargeting` so the WPF project compiles on a non-Windows host
  - Implements: `Directory.Build.props`
  - Proven by: `./build.sh` compiling the whole solution on Linux
  - Requirement: FR-010

## Phase 2 — The published artefact

- [X] **T003** Self-contained single file, compressed, embedded symbols, **not trimmed**
  - Implements: `build.sh` `publish()`, `build.ps1`
  - Proven by: the published output; **SC-004 needs Windows** to prove it runs
  - Requirements: FR-001, FR-003
- [X] **T004** `IncludeNativeLibrariesForSelfExtract` — without it a book cannot be opened
  - Implements: `build.sh`, `build.ps1`
  - Proven by: **needs Windows.** This is the one that fails at run time and not at build time.
  - Requirement: FR-002
- [X] **T005** The manifest: `asInvoker`, per-monitor-v2 DPI, long-path aware
  - Implements: `src/MyFinance.App/app.manifest`, referenced from the csproj
  - Proven by: **needs Windows**
  - Requirements: FR-004, FR-005, FR-006
- [X] **T006** Icon and product metadata
  - Implements: `src/MyFinance.App/Assets/MyFinance.ico`, `Directory.Build.props`
  - Proven by: rendered and checked at 256px and 32px — the coin was clipped by the tile edge
    at first and was moved inward
  - Requirement: FR-008
- [X] **T007** Release archive named for version and runtime identifier; missing `zip`
  degrades to a note
  - Implements: `build.sh`
  - Proven by: `artifacts/MyFinance-<version>-<rid>.zip`
  - Requirement: FR-008
- [X] **T008** Publish hygiene: no `BuildHost-*` folders, no `.lib` import libraries
  - Implements: removed `contentfiles` from `Microsoft.EntityFrameworkCore.Design`; added the
    `RemoveNativeImportLibraries` target
  - Proven by: **inspection only — nothing fails the build if a new stray file appears**
  - Requirement: NFR-001

## Phase 3 — Cross-building and the scripts

- [X] **T009** Cross-build a Windows executable from Linux, for x64 and ARM64
  - Implements: `build.sh publish [rid]`
  - Proven by: the cross-built output
  - Requirements: FR-009, FR-011
- [X] **T010** `build.cmd` shim past the execution policy; PowerShell 7 preferred, 5.1 fallback
  - Implements: `build.cmd`, `build.ps1`
  - Proven by: **needs Windows**
  - Requirement: FR-012
- [X] **T011** SDK check with the `winget` command printed
  - Implements: `build.ps1`
  - Proven by: **needs Windows**
  - Requirement: FR-013
- [X] **T012** `build ef` wrapping `dotnet-ef`, installing it on first use
  - Implements: `build.ps1`
  - Proven by: **needs Windows**; the Linux path is documented as a manual tool install
  - Requirement: FR-014

## Phase 4 — Build inputs

- [X] **T013** Model weights fetched and hash-verified, never committed
  - Implements: `tools/fetch-model.sh` and `tools/fetch-model.ps1`, `.gitignore` (`*.onnx`),
    `NOTICE` at the repository root
  - Proven by: the pinned-hash check in the script
  - Requirement: FR-015

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001, FR-003 | T003 | ✅ built; **running needs Windows** |
| FR-002 | T004 | ⚠️ **needs Windows to prove** |
| FR-004 – FR-006 | T005 | ⚠️ needs Windows |
| FR-007, FR-016 | T001 | ✅ |
| FR-008 | T006, T007 | ✅ |
| FR-009, FR-011 | T009 | ✅ |
| FR-010 | T002 | ✅ |
| FR-012 – FR-014 | T010, T011, T012 | ⚠️ needs Windows |
| FR-015 | T013 | ✅ |
| NFR-001 | T008 | ⚠️ **unenforced — inspection only** |
| NFR-002 | By construction: nothing in the runtime path opens a socket | ✅ |
