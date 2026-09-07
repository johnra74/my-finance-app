# Implementation Plan: Packaging and release

**Spec:** `./spec.md` · **Status:** As-built

## Summary

`dotnet publish` with `PublishSingleFile`, self-contained, compressed, not trimmed, driven by
two thin scripts — `build.ps1` (with a `build.cmd` shim) on Windows and `build.sh` elsewhere.
The decision this turns on: **self-contained single file with native libraries extracted**,
because the two things this application cannot run without — SQLCipher and the ONNX runtime —
are native, and the default single-file build silently leaves them behind.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 7 — nothing leaves the machine | No updater, no telemetry, no crash reporting. The published binary makes no outbound call. |
| 4 — correctness outside WPF | `EnableWindowsTargeting` lets CI compile and test everything on Linux; only *running* the UI needs Windows. |

## Technical context

- **Output:** `artifacts/publish/<rid>/MyFinance.exe`, ~85 MB, plus
  `artifacts/MyFinance-<version>-<rid>.zip`.
- **Targets:** `win-x64` (default) and `win-arm64`.
- **Version:** `Directory.Build.props` — one place, read by the publish script for the archive
  name.
- **Warnings:** `TreatWarningsAsErrors=true`, solution-wide.

## Design

### The two load-bearing publish switches

- **`IncludeNativeLibrariesForSelfExtract=true`.** Without it the application launches happily
  and then fails the moment it tries to open a book, because SQLCipher never made it into the
  bundle. The failure looks like a bug in the book-opening code, which is what makes it
  expensive.
- **No trimming.** WPF resolves a great deal by reflection that the trimmer cannot see. A
  trimmed build fails at run time, on some screens, in ways no compile-time check catches.

`EnableCompressionInSingleFile` and `DebugType=embedded` are the two that make the size
tolerable while keeping stack traces meaningful.

### `InvariantGlobalization=false`

This is a US desktop application that parses currency and dates from bank files. Invariant
globalization would strip the data those parsers rely on to save a few megabytes.

### The manifest

`asInvoker` (never elevation — this application has no reason to want it), per-monitor-v2 DPI
awareness, and long-path awareness. Books, backups and Money files all live under paths the
user chose, and a deep OneDrive folder passes 260 characters without anybody trying.

### Two scripts, one behaviour

`build.cmd` is a shim over `build.ps1` because Windows 11 ships with script execution
disabled and `.\build.ps1` fails with *"running scripts is disabled on this system"*. The shim
bypasses the policy **for this one script** and changes nothing machine-wide. It prefers
PowerShell 7 and falls back to the 5.1 that is always present, and it checks for the SDK,
printing the `winget` command when it is missing.

`build.sh` covers Linux and macOS, and cross-builds the Windows executable — a release can be
cut without a Windows machine anywhere in the loop. Only *running* the UI needs Windows.

### Publish hygiene

Two categories of stray file were found and removed:

- `BuildHost-*` folders, traced to `Microsoft.EntityFrameworkCore.Design` including
  `contentfiles`; the asset was removed. (`Microsoft.Extensions.Hosting` and `Logging.Debug`
  were dropped at the same time as unused.)
- `.lib` import libraries, removed by a `RemoveNativeImportLibraries` target filtering
  `ResolvedFileToPublish`.

Both were noticed by looking at the output rather than by any check, which is worth recording:
nothing currently fails the build if a third stray file appears.

### Model weights are a build input, not a source file

`tools/fetch-model.sh`, and `tools/fetch-model.ps1` on Windows, download them and verify
pinned hashes; `.gitignore` excludes
`*.onnx`. 23 MB of binary has no business in source history, and a hash check is what makes
"downloaded from the internet" acceptable.

### Alternatives rejected

- **Framework-dependent deployment.** Smaller, and requires the user to install a runtime
  first — which is exactly the ceremony this avoids.
- **An installer.** Nothing needs installing: no services, no registry, no shared components.
- **Trimming.** See above.
- **Committing the model weights.** See above.
- **An auto-updater.** Would mean an outbound network call from an application whose entire
  privacy story is that it makes none.

## Project structure

```
Directory.Build.props           version, product metadata, warnings-as-errors, targeting
Directory.Packages.props        central package versions
build.cmd / build.ps1           Windows entry point, SDK check, ef commands
build.sh                        Linux/macOS: build, test, publish, clean
src/MyFinance.App/app.manifest  asInvoker, per-monitor-v2 DPI, long paths
src/MyFinance.App/Assets/MyFinance.ico
tools/fetch-model.sh|.ps1|.cmd  model weights, pinned hashes
```

## Risks

- **Publish hygiene is unenforced.** Two stray-file classes were found by inspection; a third
  would not fail anything. A check over the publish manifest would close this.
- **The application name is baked in** — executable name, icon, manifest, product metadata.
  Renaming is a multi-file change, and the name is still a placeholder.
- **Unsigned.** An executable downloaded from the internet will produce a SmartScreen warning.
  Fine for one user on their own machine; a barrier to giving it to anyone else.
- **No update path.** Every upgrade is a manual file copy, and nothing tells the user a new
  version exists.
