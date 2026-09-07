# Feature Specification: The encrypted book

**Folder:** `001-encrypted-book`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "Single-user, password-protected personal finance records that nobody but the
owner can read, including anybody who copies the file."

## User Scenarios & Testing

### Primary user story

A person keeps their entire financial history — every account, every payee, twenty-five
years of transactions — in one file on their own machine. They want that file to be
worthless to anyone who copies it: a stolen laptop, a synced cloud folder, an old backup
drive sold on. They set a password when they create the book, and type it each time they
open it. Nobody else, including the people who wrote this application, can recover it.

### Acceptance scenarios

1. **Given** no book exists at a chosen location, **When** the user creates one with a
   password, **Then** a book is created with a usable starting chart of accounts, and the
   application states plainly that a forgotten password means the data is gone.
2. **Given** an existing book, **When** the correct password is entered, **Then** the book
   opens.
3. **Given** an existing book, **When** a password differing by a single character is
   entered, **Then** the book is refused, with no hint about which part was wrong.
4. **Given** a book file copied off the machine, **When** it is opened in any text or
   database tool, **Then** no account name, payee or amount is readable.
5. **Given** an open book, **When** the user changes the password, **Then** only the new
   password opens it afterwards, and the old one is refused.
6. **Given** the user is choosing a password, **When** they type, **Then** they are shown a
   strength indication, because this is a secret they can never recover.

### Edge cases

- The sidecar file is missing → reported as *the book is missing*, not as a wrong password.
  The two failures need different actions from the user, and conflating them sends people
  hunting for a password when the real problem is a lost file.
- The database file is present but the sidecar is not → same: the book is incomplete.
- Creation fails part-way → neither half is left behind.
- A sidecar written by a newer version of the application → refused explicitly, rather than
  misread under this version's assumptions.
- An empty password → refused at creation.
- Creating a book where one already exists → refused rather than overwriting.

## Requirements

### Functional requirements

- **FR-001**: The system MUST encrypt the entire book at rest, including account names,
  payees, memos and amounts.
- **FR-002**: The system MUST derive the encryption key from the user's password with a
  memory-hard key derivation function, using a random per-book salt.
- **FR-003**: The system MUST NOT store the password, or any value derived from it that
  could verify a guess, anywhere.
- **FR-004**: The system MUST store the key-derivation salt and cost parameters outside the
  encrypted database, since they are required before it can be opened.
- **FR-005**: The system MUST record the cost parameters per book, so that costs can be
  raised for new books later without stranding existing ones.
- **FR-006**: The system MUST reject an incorrect password by proving it cannot read the
  data, not merely by accepting the key.
- **FR-007**: The system MUST distinguish "book not found" from "wrong password" in what it
  reports.
- **FR-008**: The system MUST seed a new book with a usable starting chart of accounts.
- **FR-009**: The system MUST allow the password to be changed, re-encrypting the book under
  a new key derived from a fresh salt.
- **FR-010**: The system MUST leave the old password working if a password change fails
  part-way through.
- **FR-011**: The system MUST estimate and display password strength while one is being
  chosen, as guidance rather than as an enforced rule.
- **FR-012**: The system MUST NOT offer any password recovery, reset or hint mechanism.
- **FR-013**: The system MUST refuse to create a book over an existing one.
- **FR-014**: The system MUST leave no partial book behind when creation fails.
- **FR-015**: The system MUST refuse a metadata sidecar it does not understand — a newer
  format version, an unknown derivation function, or malformed contents — rather than
  guessing at its meaning.

### Non-functional requirements

- **NFR-001**: Unlocking MUST cost roughly half a second on a typical desktop. That delay is
  the defence: it is multiplied across an attacker's entire guess space.
- **NFR-002**: Key derivation MUST be memory-hard enough to deny an attacker cheap GPU
  parallelism — at least tens of mebibytes per guess.
- **NFR-003**: The password's in-memory byte copy MUST be zeroed after use, and the derived
  key MUST be zeroed when the book is closed.

## Key Entities

- **Book** — the open handle to one person's records: a database file, a derived key, and
  the parameters that produced it. Disposing it destroys the key and refuses further access.
- **Key parameters sidecar** — the non-secret half of the derivation: salt, cost parameters,
  key length, format version, creation date. Public by design; a salt's job is to be unique,
  not hidden.

## Success Criteria

- **SC-001**: An account name saved into a book does not appear in plaintext anywhere in the
  file on disk. *Asserted by `BookFileServiceTests.Account_names_do_not_appear_in_plaintext_on_disk`.*
- **SC-002**: The correct password opens a book; a password differing by one character does
  not. *`A_book_reopens_with_the_correct_password`, `A_password_differing_by_one_character_is_rejected`.*
- **SC-003**: Two books created with the same password have different keys, because each has
  its own salt. *`The_same_password_under_a_different_salt_derives_a_different_key`.*
- **SC-004**: After a password change, only the new password opens the book, and the salt has
  changed. *`Changing_the_password_switches_which_password_opens_the_book`,
  `Changing_the_password_generates_a_fresh_salt`.*
- **SC-005**: A failed creation leaves neither file behind.
  *`A_failed_creation_leaves_no_half_written_book_behind`.*
- **SC-006**: A missing book and a wrong password produce different, distinguishable
  outcomes. *`Opening_a_missing_book_reports_it_as_missing_not_as_a_bad_password`.*
- **SC-007**: A sidecar from a newer format version, naming an unknown derivation function,
  or malformed, is refused rather than misread. *Three tests in `BookKeyDerivationTests`.*
- **SC-008**: An amount survives a save and reload exactly, including its sign.
  *`Money_survives_a_save_and_reload_exactly`, `A_negative_balance_round_trips_with_its_sign`.*

## Assumptions

- Depends on constitution principles **6** (store nothing you do not need), **7** (nothing
  leaves the machine), **1** (integer cents), **3** (every write through a service).
- Single user, single concurrent session. No sharing, no multi-user access control, no roles.
- The threat model is **an attacker holding a copy of the file, guessing offline**. It is not
  an attacker with live access to the unlocked machine — once the book is open in memory,
  nothing here defends against code running as that user.
- The user is responsible for keeping both files together. The application's answer to that
  responsibility is `010-backup-and-restore`, not a second copy of the salt.

## Out of Scope

- Password recovery, escrow, hints, or security questions. Deliberately, permanently.
- Multi-user access, roles or per-account permissions.
- Locking on idle or re-prompting mid-session. *(Unspecified today — see the gap table in
  `specs/README.md`.)*
- Hardware key or biometric unlock.
- What happens when a book written by a newer schema is opened by an older build — see
  `017-book-schema-upgrade`.
