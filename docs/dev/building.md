# Building

The prerequisite everywhere is the **.NET 10 SDK**.

## Windows — the full experience

This is the only platform where the WPF application actually runs.

```text
build            :: build + test  (the default)
build run        :: launch the app
build publish    :: self-contained single-file .exe in artifacts\publish
build test
build clean
```

Every script in this repository has a Windows counterpart and a `.cmd` shim beside it;
`ScriptParityTests` fails the build if one is added without them.

`build.cmd` is a thin shim over `build.ps1`. **Use it rather than calling the `.ps1`
directly**: Windows 11 ships with script execution disabled, so `.\build.ps1` fails with
*"running scripts is disabled on this system"*. The shim bypasses that policy for this one
script and changes nothing machine-wide. It prefers PowerShell 7 and falls back to the
Windows PowerShell 5.1 that is always present.

To call the script directly instead, either use PowerShell 7 (`pwsh .\build.ps1`) or allow
local scripts once:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

The script checks for the SDK and prints the `winget` command if it is missing:

```powershell
winget install Microsoft.DotNet.SDK.10
```

## Linux and macOS — engine work and CI

```bash
./build.sh          # build + test
./build.sh test     # tests only
./build.sh clean
```

`Directory.Build.props` sets `EnableWindowsTargeting`, so the WPF project **compiles** on a
non-Windows host and CI can gate on the whole solution. It cannot run there.

Everything that has to be provably correct lives in the platform-neutral projects, which is
why the test suites need no Windows at all.

## The embedding model

The weights are not in this repository. Fetch them with the script for your platform, which
downloads them and checks them against pinned hashes:

::::{tab-set}
:::{tab-item} Windows
```text
tools\fetch-model
```
:::
:::{tab-item} Linux and macOS
```bash
./tools/fetch-model.sh
```
:::
::::

Both print the same lines, verify the same digests and are interchangeable. As with `build`,
the Windows entry point is the `.cmd` shim rather than the `.ps1`, for the same
execution-policy reason.

The model's licence, and everything else the executable carries, is in `NOTICE` at the
repository root — see {doc}`licensing`.

The application does not need them to build or to run — if the model or its native runtime
will not load, the merchant-resemblance suggestion source quietly disappears and the others
carry on.
