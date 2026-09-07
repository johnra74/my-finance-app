# Backups

There is no password recovery, no copy of your book on anybody's server, and no way to get
one back if it is lost. **Backups are not an afterthought here.**

## What has to be backed up

A book is **two files**:

| | |
|---|---|
| `name`{{ book_ext }} | The encrypted database |
| `name`{{ sidecar_ext }} | A small plaintext sidecar holding the salt the key is derived from |

**Both are needed to open it.** A backup that copies only the database is not a backup —
which is the whole reason MyFinance writes one {{ backup_ext }} archive holding the pair,
rather than leaving people to copy files themselves.

## Automatic

**On close**, switched on for a new book, keeping the newest ten in a `Backups` folder
beside the book. Both the count and the folder can be changed under **Backups** in the top
bar.

A backup that fails never prevents you closing the book — it says so instead. An application
that refuses to shut down is worse than a missed backup.

## Manual

- **Back up now**, at any time.
- **Back up to…** somewhere of your own. **Nothing here will ever prune a folder you chose** —
  a copy you put on a memory stick is yours.

## Why these backups can be trusted

- **The write-ahead log is folded in first.** Without that step a copy would be missing
  everything done since the last checkpoint — precisely the session you are trying to
  protect.
- **Every archive is read back before it is accepted.** A backup nobody has read is a guess,
  and the second spent checking catches a full disk or a failing drive at the only moment
  anything can still be done about it.

## Keeping one somewhere else

A {{ backup_ext }} needs no special handling. The database inside is still encrypted under
the same password and the sidecar holds only a salt, so it is **exactly as safe in cloud
storage as the book itself**. Put one somewhere that is not this machine.

## Restoring

Offered on the **opening screen**, not from inside an open book — that is the only moment it
is safe. Replacing the file underneath a running session would leave it writing into a
database that no longer exists.

:::{warning}
A restored book opens with the password it had **when the backup was taken**. A backup
cannot help somebody who has forgotten their password, and a backup taken before you changed
your password still wants the old one.
:::

## What backups cannot do

| | |
|---|---|
| Recover a forgotten password | No. Nothing can. |
| Replace the sidecar you deleted | Only if a backup containing it still exists. |
| Repair a corrupted book | Restore the last good one. Nothing checks a book for corruption, so keep enough history to have a good one. |
