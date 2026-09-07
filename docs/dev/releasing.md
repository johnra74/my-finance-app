# Cutting a release

```bash
./build.sh publish            # a Windows .exe, cross-built
./build.sh publish win-arm64  # or for an ARM machine
```

The Windows executable cross-builds from Linux, so a release can be cut without a Windows
machine anywhere in the loop. Only *running* the UI needs Windows.

## What comes out

**One file**, `artifacts/publish/win-x64/MyFinance.exe`, at roughly 85 MB. No installer and
nothing to install: it is self-contained, so the machine needs no .NET runtime, and it can be
copied wherever the user likes.

Alongside it the script writes `artifacts/MyFinance-<version>-<rid>.zip`, which is the whole
release.

## Two build settings that are load-bearing

:::{warning}
- **`IncludeNativeLibrariesForSelfExtract`** — SQLCipher and ONNX Runtime both ship as native
  libraries. Without it the single file launches happily and then fails the moment it tries
  to open a book.
- **No trimming.** WPF resolves a great deal by reflection that the trimmer cannot see.
:::

## The manifest

The executable requests `asInvoker` and **never** elevation, declares per-monitor-v2 DPI
awareness, and is long-path aware — because books, backups and Money files live under paths
the user chose, and a deep OneDrive folder passes 260 characters without anybody trying.

## Version

`Directory.Build.props` holds `<Version>`, and it is the single source: the zip name takes it,
and `docs/conf.py` reads it out of that file rather than keeping a copy.

## Checklist

1. `./build.sh` — every suite green, **zero warnings**.
2. Bump `<Version>` in `Directory.Build.props`.
3. `./build.sh publish`.
4. Confirm the zip contains one `.exe` and no strays.
5. On Windows: create a book, open an existing one, and — if a migration is in this release —
   upgrade a copy of a real book and confirm the backup was written first.
6. Confirm the docs build clean: `sphinx-build -W -b html docs docs/_build/html`.
