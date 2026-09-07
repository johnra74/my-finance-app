# When something goes wrong

## Finding this documentation again

{kbd}`F1`, or the **Help** button in the top bar, opens the page about whatever screen you are
on. It works on the unlock screen as well, before any book is open.

The pages are carried **inside the executable** and read from your own machine — no connection
is needed, and they always describe the build you are running rather than the newest one. They
are unpacked on first use to `%LOCALAPPDATA%\MyFinance\help`.

:::{note}
If Help says *"this build of MyFinance was made without its documentation"*, the executable
was built on a machine without the documentation toolchain. The pages are still readable as
Markdown in the `docs` folder of the source repository.
:::

## Which version am I running

**About** in the top bar gives the version number, and the same button is on the unlock
screen, so it can be read without opening a book.

It also shows the licence and what this build is made of. MyFinance ships as one file that
carries its database engine, its encryption, the suggestion model and these pages, and About
is where the licences for all of them are reproduced — readable and copyable, without needing
the source repository.

## The diagnostics log

MyFinance records what goes wrong to a log file, so a fault can be looked into afterwards.

**Where:** `%LOCALAPPDATA%\MyFinance\logs`

**Diagnostics** in the top bar opens that folder in one click, and offers to turn on extra
detail.

The log is bounded by size — two files of about a megabyte, the current one and the previous
— so a fault repeating in a loop cannot fill your disk. Repeated identical failures are
collapsed rather than written out a thousand times.

### Extra detail

If a problem keeps happening, turn on extra detail before reproducing it. It records **which
operations ran and in what order**, which makes an intermittent fault far easier to find.

It still records nothing about what is in your book. The redaction rules hold absolutely at
every level: extra detail adds context about what the application *did*, never about what
your book *contains*.

### What it is safe to share

The log is designed to be shareable. It contains no payee, no category, no account name, no
memo, no amount and no path — see {doc}`security`. Read it first if you like; it is plain
text.

**It is never transmitted anywhere by MyFinance.** Sending it is your act.

## Common situations

:::{dropdown} "This book was created by a newer version"
The book was last written by a build newer than the one you are running, and opening it is
refused rather than risked. An older build would read the columns it recognises, ignore the
ones it does not, and write into the one file that has no recovery path.

Get the newer executable, or restore a backup taken before the upgrade.
:::

:::{dropdown} The book needs upgrading
Written by an older build. MyFinance offers to upgrade it, takes a backup **and verifies it**
first, and refuses to proceed if that backup cannot be written.

The upgrade is applied to a copy which replaces the original only once every step has
succeeded, so an upgrade that fails leaves the book exactly as it was.
:::

:::{dropdown} A statement imported backwards
Every payment shows as a deposit and vice versa. Undo the import from **Import history**,
then import again, using the reverse switch on the preview. Compare the two balances the
preview shows before you accept it — see {doc}`importing`.
:::

:::{dropdown} The migrated balances do not match Money
Almost always the projected-but-never-entered bills. Money counts them; a book left unused
for a while shows a balance far below its real one because the bills kept projecting forward
and the salary did not. See {doc}`migrating`.
:::

:::{dropdown} A page does not refresh, and nothing is shown
That is the signature of an exception inside work nobody awaited. Turn on extra detail,
reproduce it, and look in the log for `UnobservedTask`.
:::

:::{dropdown} Suggestions have stopped appearing
If the local model or its native runtime will not load, the feature disappears quietly and
the other sources carry on. On a new book with no history there is nothing to suggest from,
and MyFinance says nothing rather than inventing an answer.
:::

## Reporting a fault

There is nobody at the other end of an automatic report, because nothing is transmitted. A
useful report by hand is:

1. What you were doing.
2. What you expected, and what happened.
3. The relevant entries from the log — including the version line at the top.
4. Whether it happens every time.
