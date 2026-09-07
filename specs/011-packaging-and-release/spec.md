# Feature Specification: Packaging and release

**Folder:** `011-packaging-and-release`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "Ship this as something I can copy onto a machine and run. No installer, no
runtime to install first."

## User Scenarios & Testing

### Primary user story

The user wants the application on their Windows machine without an installation ceremony,
without administrator rights, and without first installing a .NET runtime. They should be able
to copy one file wherever they like — including onto a memory stick — and run it. A developer
should be able to cut that release without owning a Windows machine.

### Acceptance scenarios

1. **Given** a Windows machine with no .NET runtime, **When** the executable is copied there
   and run, **Then** it starts.
2. **Given** the running application, **When** a book is opened, **Then** the native
   encryption library loads. *(This is the specific failure that a naive single-file build
   produces: it launches happily and fails the moment it touches a book.)*
3. **Given** a Linux or macOS machine, **When** the publish script is run, **Then** a Windows
   executable is produced.
4. **Given** any supported host, **When** the build script runs, **Then** the whole solution —
   including the WPF project — compiles and every test runs.
5. **Given** a fresh Windows 11 machine, **When** the user runs `build`, **Then** it works
   despite the default script-execution policy, and says how to install the SDK if it is
   missing.
6. **Given** a deep path — a nested OneDrive folder, say — **When** a book or backup is placed
   there, **Then** the application handles it.

### Edge cases

- The machine's script execution policy blocks `.ps1` → a `.cmd` shim beside each one
  bypasses the policy for that script alone and changes nothing machine-wide.
- PowerShell 7 absent → falls back to the Windows PowerShell 5.1 that is always present.
- `zip` absent on a Linux host → the folder is left unpacked with a note, rather than failing
  the publish.
- A path beyond 260 characters → handled; books, backups and Money files all live under paths
  the user chose.
- A high-DPI or mixed-DPI display → declared per-monitor-v2.

## Requirements

### Functional requirements

- **FR-001**: The release MUST be **one self-contained file**, requiring no .NET runtime on
  the target machine.
- **FR-002**: The build MUST include native libraries for self-extraction. SQLCipher and the
  ONNX runtime both ship as native libraries.
- **FR-003**: The build MUST NOT be trimmed. WPF resolves a great deal by reflection that the
  trimmer cannot see.
- **FR-004**: The executable MUST request `asInvoker` and never elevation.
- **FR-005**: The executable MUST declare per-monitor-v2 DPI awareness.
- **FR-006**: The executable MUST be long-path aware.
- **FR-007**: The build MUST keep real globalization data, for currency and date parsing.
- **FR-008**: The build MUST produce a release archive alongside the executable, named for the
  version and the runtime identifier.
- **FR-009**: The Windows executable MUST cross-build from Linux, so a release can be cut with
  no Windows machine in the loop.
- **FR-010**: The whole solution, WPF included, MUST compile on a non-Windows host, so CI can
  gate on all of it.
- **FR-011**: The build MUST support x64 and ARM64 Windows targets.
- **FR-012**: The Windows entry point MUST work under the default execution policy, and MUST
  prefer PowerShell 7 while falling back to 5.1.
- **FR-012a**: **Every** script in the repository MUST have a Windows counterpart with a
  `.cmd` shim, not only the build entry point. *Development happens on Linux and the
  application only runs on Windows, so the Windows scripts are the ones nobody exercises by
  accident — `tools/fetch-model.sh` had no counterpart for months, which left a Windows-only
  developer unable to fetch the embedding model at all, with nothing to say so.*
  `ScriptParityTests` enforces this, along with build.ps1 offering every task build.sh does.
- **FR-013**: The build MUST check for the .NET SDK and print the command to install it.
- **FR-014**: The build MUST offer database-migration commands, installing the tool on first
  use where it can.
- **FR-015**: The model weights MUST NOT be in the repository; they MUST be fetched by a
  script and verified against pinned hashes.
- **FR-016**: Warnings MUST be treated as errors across the solution.

### Non-functional requirements

- **NFR-001**: The published executable SHOULD stay around 85 MB, and the publish output MUST
  contain nothing but what is needed to run.
- **NFR-002**: No build step may require network access at run time, and none may contact any
  service on the user's behalf.

## Key Entities

None. This feature owns no data, and therefore has no `data-model.md`.

## Success Criteria

- **SC-001**: `./build.sh` builds and tests the whole solution on Linux: **819 passed, 17
  skipped, 0 failed, 0 warnings**. The 17 skip on a machine without the user's `.mny` and the
  private OFX fixtures — both gitignored under constitution 8 — so a clean checkout cannot run
  them, and a criterion of "836 passing" would be unreachable by anyone but the author.
- **SC-002**: `./build.sh publish` produces `artifacts/publish/win-x64/MyFinance.exe` at
  roughly 86 MB, plus `artifacts/MyFinance-<version>-<rid>.zip` holding it together with
  `LICENSE` and `NOTICE`.

  :::{note}
  **This was not actually true until 2026-09-07.** The archive step was missing `zip -r` and
  wrote to a relative path one level short, so on a Linux host it failed twice over — and both
  failures fell into a fallback that reported `'zip' is not installed`, which was untrue and
  looked benign. No release archive was ever produced there. Found by running the step to
  check that the licence files reached it. FR-008 had been unmet for as long as the script has
  existed.
  :::
- **SC-003**: The publish output contains no stray files. *Two were found and removed during
  development — `BuildHost-*` folders pulled in by `Microsoft.EntityFrameworkCore.Design`'s
  `contentfiles`, and `.lib` import libraries filtered out by an explicit publish target.*
- **SC-004**: A published executable opens a book on a machine with no .NET runtime — which is
  what proves FR-002 rather than merely asserting it. **Needs Windows.**
- **SC-005**: `build.cmd` runs on a stock Windows 11 machine with script execution disabled.
  **Needs Windows.**
- **SC-006**: `build.cmd publish` completes on Windows.

  :::{note}
  **This could never have happened until 2026-09-07.** The four MSBuild properties on the
  publish command were unquoted, and PowerShell reads an unquoted `-p:Name=Value` as its own
  `-parameter:value` syntax: it bound `-p` to the common parameter `-PipelineVariable` and
  refused with *"Cannot bind parameter because parameter 'p' is specified more than once"*
  before `dotnet` was ever invoked. Reported from a Windows machine; the Linux script was
  unaffected, because the identical line there is ordinary shell.

  `PowerShellArgumentTests` now fails the build for an unquoted `-name:value` in any `.ps1`.
  The message differs by host — Windows PowerShell 5.1 says *specified more than once*, while
  PowerShell 7.4+ says *ambiguous ... -ProgressAction, -PipelineVariable* — so the symptom
  depends on which shell `build.cmd` falls back to.
  :::

## What the executable carries

Beyond the application and its runtime, the single file also carries:

- **The embedding model** — about 22 MB, optional. See `005-category-suggestion`.
- **The documentation** — about 284 KB, optional. See `019-in-application-help`. The
  documentation toolchain (Python and Sphinx) is therefore a **publish-time** dependency, and
  a deliberately soft one: a machine without it still builds, tests and publishes, and Help
  reports that the pages are absent rather than the build failing. A release without its
  documentation is worse than one with it, and much better than no release at all.
- **`LICENSE` and `NOTICE`** — a few kilobytes, **not optional**. SQLCipher's BSD-3 terms and
  the Apache-2.0 terms on SQLitePCLRaw and the embedding model each require their copyright
  notice to be reproduced in a binary distribution. A single self-contained file *is* the
  distribution, so the notice is embedded in it and shown by **About**, as well as copied
  beside the executable by `build publish` for anyone who never launches it. `NoticeTests`
  fails the build for any declared package the notice does not name.

## Assumptions

- Depends on constitution principles **7** (nothing leaves the machine — there is no updater
  and no telemetry), **4** (the platform-neutral projects are what CI can actually verify).
- Windows 11 is the target. Only `MyFinance.App` targets Windows; everything else is
  platform-neutral.
- The application name `MyFinance` is a placeholder that has now been baked into the
  executable name, icon, manifest and product metadata. **Renaming it later is a
  multi-file change.** `[NEEDS CLARIFICATION: is MyFinance the final name?]`

## Out of Scope

- An installer, an MSI, or a Store package.
- Code signing. *(Unsigned, an executable downloaded from the internet gets a SmartScreen
  warning — worth knowing before distributing it to anyone else.)*
- Automatic updates. See the gap table in `specs/README.md`.
- macOS or Linux builds of the interface. The engine is platform-neutral, but the UI is WPF.
