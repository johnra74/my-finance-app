# How a book is protected

## The short version

Your book is encrypted with a 256-bit key derived from your password every time you open it.
The password is never stored, in any form. There is no recovery path, on purpose.

## The mechanism

| | |
|---|---|
| **Key derivation** | Argon2id — 4 iterations, 64 MiB, 2 lanes |
| **Database encryption** | SQLCipher, taking that key as a raw key |
| **Where the salt lives** | The plaintext {{ sidecar_ext }} sidecar |
| **Where the password lives** | Nowhere |

The salt is in the sidecar because it is needed **before** the database can be opened. A
salt's job is to be unique, not secret.

Unlocking deliberately takes about half a second. That cost is what makes offline guessing
expensive for somebody who has stolen the file: 64 MiB and four passes per attempt is cheap
once and ruinous a billion times.

## Consequences, stated plainly

:::{danger}
- **There is no password recovery.** Forgetting the password destroys the data.
- **Losing the sidecar is equally fatal.** Without the salt the key cannot be re-derived.
- Neither of these is a bug or an oversight, and neither has a workaround.
:::

This is why {doc}`backups` archives the pair together rather than trusting anybody to
remember the second file.

## What is not stored

- **Your password**, in any form.
- **Full account numbers.** Only a masked form for display, plus a keyed digest under a
  per-book secret so a statement can be matched to its account.
- **Bank credentials.** MyFinance imports files and never connects to a bank, so there is
  nothing to collect and nowhere to put it.

## Nothing leaves the machine

No telemetry, no crash reporting, no update check, no price feed, no network calls of any
kind. The local embedding model that recognises unfamiliar merchants runs on your machine.
The diagnostics log is written to disk and never transmitted.

Sending anything is **your** act, never the application's.

## What the diagnostics log contains

When something goes wrong, MyFinance records enough to investigate it and nothing about your
money. The log never contains payee names, category names, account names, memos, amounts, or
any transaction detail — and it never contains your book's path or even its file name, which
you chose and which can be as revealing as the contents. It records a non-reversible hash
instead, enough to tell two books' entries apart while identifying neither.

That is enforced by the design rather than by a rule somebody has to remember: the writer
accepts a fixed vocabulary of operation names and offers no parameter that could carry your
data. See {doc}`troubleshooting`.

## What this does not protect against

- **Someone at your unlocked machine.** Nothing re-asks for your password while the book is
  open.
- **A keylogger or a compromised Windows account.** Disk encryption cannot help once your
  password has been typed on a machine somebody else controls.
- **Two copies of MyFinance open on the same book.** Nothing prevents it, and it can damage
  the book. Open one at a time.
