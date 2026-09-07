# Your first book

## Creating one

On the opening screen, choose **Create a new book**, pick where to save it, and set a
password.

:::{danger}
**The password cannot be recovered.** It is never stored — not encrypted, not hashed, not
hinted. The key that decrypts your book is derived from it every time you open one. If you
forget it, the data is gone, and no amount of access to the file will bring it back.

The application asks you to confirm you understand this before it creates the book, because
it is the one decision here that cannot be undone.
:::

Choose something you will still know in five years and write it down somewhere physical.
A password manager is the obvious answer; a sealed envelope in a drawer is a real one.

## What gets created

Two files, in the folder you chose:

| File | What it is | Secret? |
|---|---|---|
| `yourbook`{{ book_ext }} | The encrypted database. Everything you enter lives here. | Yes |
| `yourbook`{{ sidecar_ext }} | A small plaintext file holding the salt and cost settings your key is derived from. | No — but **required** |

**Both are needed to open the book.** The sidecar holds no secret; a salt's job is to be
unique, not hidden. But without it the key cannot be re-derived, so losing it is exactly as
fatal as forgetting the password. This is why the application backs the pair up as one
archive rather than trusting anybody to remember the second file.

## What a new book starts with

A usable two-level chart of categories, seeded once. You can rename, re-parent, archive,
delete and merge any of it.

If you are bringing a Microsoft Money file across, **do that before entering anything**:
migration writes into an empty book only. Go to {doc}`migrating` now. Your Money categories
replace the seeded ones.

## Opening it again

Choose the book on the opening screen and type your password. Unlocking takes about half a
second, and that slowness is the point: it is what makes guessing your password expensive
for somebody who has stolen the file. See {doc}`security`.

## Locking it

**Lock** in the top bar closes the book and returns to the opening screen, taking a backup
on the way out if automatic backups are on.

:::{note}
Nothing re-asks for your password while the book is open. A session left unattended on an
unlocked machine is readable by whoever is sitting at it. Lock it, or lock Windows.
:::

## Changing your password

Available while a book is open. It re-encrypts the database under a new key and only then
rewrites the sidecar — so if anything fails part-way, the old password still opens the book.

:::{warning}
Backups taken **before** the change still open with the **old** password. A backup carries
the salt it was made with. Keep track of which is which, or take a fresh backup immediately
after changing it.
:::
