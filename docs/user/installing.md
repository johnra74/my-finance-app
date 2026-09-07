# Installing

There is no installer and nothing to install.

## What you download

One file, `MyFinance.exe`, about 85 MB, inside `MyFinance-{{ release }}-win-x64.zip`.
Unzip it and put the executable wherever you like — a folder in your documents, a memory
stick, a network share. It runs from there.

It is **self-contained**: the machine needs no .NET runtime and no other component. That
is why it is 85 MB rather than 2 MB.

## Requirements

| | |
|---|---|
| **Operating system** | Windows 10 or 11 |
| **Architecture** | x64, or ARM64 from the `win-arm64` build |
| **Runtime** | None. Everything is in the file. |
| **Privileges** | None. It requests `asInvoker` and never asks to elevate. |
| **Network** | None, ever. See {doc}`security`. |

The executable declares per-monitor-v2 DPI awareness, so it is sharp on a high-resolution
display, and is long-path aware, because books and backups end up under folders you chose
and a deep OneDrive path passes 260 characters without anyone trying.

## Where things end up

Nothing is written outside the two places below.

| What | Where |
|---|---|
| Your book | Wherever you saved it. The application never chooses for you. |
| Backups | A `Backups` folder beside the book, unless you change it |
| The diagnostics log | `%LOCALAPPDATA%\MyFinance\logs` |

The log is deliberately **not** kept beside the book: books often live in a synced folder,
and a log written there would be copied off the machine as a side effect of where the book
happens to sit.

## Upgrading

Replace the executable with the new one. There is no updater and nothing tells you a new
version exists.

:::{warning}
Opening a book with an **older** build than the one that last wrote it is refused, with an
explanation. That refusal is deliberate: an older build would read the columns it
recognises, ignore the ones it does not, and then write into the one file that has no
recovery path. Keep the newer executable, or restore a backup taken before the upgrade.

Opening a book written by an **older** build works: it is upgraded, after a backup is taken
and verified. See {doc}`../dev/schema-migrations`.
:::

## Uninstalling

Delete the executable. Your book, your backups and your log are yours and are left alone;
delete them yourself if that is what you want.
