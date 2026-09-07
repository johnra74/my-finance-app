# Feature Specification: A diagnostics log

**Folder:** `018-diagnostics-log`
**Created:** 2026-09-06
**Status:** **Implemented** (2026-09-06)
**Input:** "The solution runs into occasional runtime exceptions. There don't seem to be any
logs to help investigate the root cause."

## Why this is a gap

**Nothing in this application writes a log.** `Microsoft.Extensions.Logging` is referenced by
two projects and **no code calls it** — no provider, no sink, not one `LogError`. What exists
instead:

| Path an exception can take | What happens today |
|---|---|
| Thrown on the UI thread | `App.OnDispatcherUnhandledException` shows a message box containing `e.Exception.Message` — **no type, no stack, no context** — then sets `Handled = true` and the exception is gone. |
| Thrown on a background thread | **No `AppDomain.UnhandledException` hook.** The process dies with nothing recorded. |
| Thrown inside a fire-and-forget task | **No `TaskScheduler.UnobservedTaskException` hook**, and there are **20+ `_ = SomethingAsync()` call sites** — every page's filter handler, `NavigationService.OnNavigatedToAsync`, `ShellViewModel.Start`'s auto-entry and index build. The task faults, nobody observes it, and the exception is **dropped in silence**. The page simply does not refresh. |
| Caught deliberately | 22 `ShowError` sites and 59 `catch` blocks. Each turns an exception into a sentence for the user and discards everything a developer would need. |

So the reported symptom is exactly what the design produces: a message appears, the user clicks
OK, and there is nothing left to investigate with. In the fire-and-forget case there is not
even a message.

This is worse in this application than it would be elsewhere: the book is encrypted, single
user, on one machine, and a failure cannot be reproduced by anyone but its owner. Without a
log, "it did it again yesterday" is the entire bug report.

## User Scenarios & Testing

### Primary user story

Something fails intermittently. The user wants to carry on working, and afterwards be able to
find a file that says what went wrong, when, and what they were doing — enough to describe the
fault to somebody who can fix it, without that file becoming a second copy of their financial
records.

### Acceptance scenarios

1. **Given** an exception on the UI thread, **When** it is handled, **Then** the message box
   still appears **and** an entry is written recording the exception type, message, stack trace
   and the operation in progress.
2. **Given** an exception on a background thread, **When** it goes unhandled, **Then** it is
   recorded before the process ends.
3. **Given** an exception inside a fire-and-forget task, **When** nobody observes it, **Then**
   it is recorded rather than dropped.
4. **Given** a failure the user wants to report, **When** they look for the log, **Then** the
   application can show them where it is in one action.
5. **Given** a log with a month of entries, **When** it is read, **Then** it contains no payee
   name, no amount, no account name and no category — nothing that would tell a reader what the
   user spends money on.
6. **Given** a long-running session, **When** the log grows, **Then** it is bounded: old
   entries are discarded rather than filling the disk.

### Edge cases

- The exception happens while writing the log → logging must never itself become the failure
  that takes the application down.
- The book folder is read-only, full, or on a disconnected drive → the application still runs.
- Two copies running at once (the executable is a single file people copy around) → neither
  corrupts the other's log.
- An exception during startup, before services exist → still recorded.
- An exception in the same place a thousand times → the log must not become a thousand
  identical entries that bury everything else.

## Requirements

### Functional requirements

**Catching everything**

- **FR-001**: The system MUST record unhandled exceptions from the UI thread, from background
  threads, and from unobserved tasks. All three, because all three happen today and only the
  first is noticed at all.
- **FR-002**: The system MUST record an entry when a deliberate `catch` reports a failure to
  the user, so a message the user saw has a counterpart a developer can read.
- **FR-003**: An entry MUST carry the exception type, message, stack trace, inner exceptions,
  a timestamp, and what the application was doing.
- **FR-004**: The system MUST record the application version and the schema version of the open
  book, because "which build was this" is the first question about any fault report.
- **FR-005**: The system MUST NOT change what the user sees. The message box, the rolled-back
  transaction and the surviving session all stay as they are; the log is added beside them.

**Not becoming a second copy of the user's records**

- **FR-006**: The log MUST NOT contain payee names, category names, account names, memos,
  amounts, or any transaction detail.
- **FR-007**: The log MUST NOT contain the password, any key material, or the contents of the
  key sidecar.
- **FR-008**: Where an identifier is needed to make an entry useful, the system MUST record a
  non-identifying one — a row id or an account id rather than a name.
- **FR-009**: The system MUST NOT record the book's path **or its file name**. It MUST instead
  record a stable non-reversible hash, so entries from two different books can be told apart
  while neither is identified. A name the user chose can be as revealing as the contents — a
  book called "Divorce settlement" is sensitive before it is opened.
- **FR-010**: The system MUST NOT transmit the log anywhere. Constitution 7 — sending it is the
  user's act, not the application's.

**Finding it and keeping it manageable**

- **FR-011**: The system MUST be able to reveal the log's location to the user in one action.
- **FR-012**: The log MUST be bounded by **size**, not by age: two files of about a megabyte,
  the current one and the previous, rolling over when the first fills. A fault repeating in a
  tight loop must not be able to fill a disk, which an age-based policy would allow.
- **FR-013**: A failure to write the log MUST NOT surface to the user or affect the operation
  in progress.
- **FR-014**: The system MUST collapse repeated identical failures rather than writing each one
  in full.
- **FR-015**: The system MUST offer a **verbose** mode the user can turn on to reproduce an
  intermittent fault. It records which operations ran and in what order — breadcrumbs, not
  data. **FR-006 to FR-010 hold absolutely at every level**: verbose adds context about what
  the application did, never about what the book contains.
- **FR-016**: The log MUST live in the user's local application data, not beside the book.
  Books commonly sit in synced folders, and a log written there would be copied off the machine
  as a side effect of where the book happens to live.
- **FR-017**: The book tag of FR-009 MUST be recorded for a book that was just **created**, not
  only for one that was unlocked. A session that begins by creating a book is still a session
  whose failures have to be told apart from another book's, and its first minutes — the seed,
  the first import, a migration — are where a new fault is most likely.

### Non-functional requirements

- **NFR-001**: Writing an entry MUST NOT block the interface thread.
- **NFR-002**: The log MUST survive the process dying — an entry written before a crash must be
  on disk after it.
- **NFR-003**: Logging MUST add no dependency that would break the single-file build, which
  ships no runtime and resolves nothing by reflection that the publish step cannot see.

## Key Entities

- **Log entry** — timestamp, severity, the operation in progress, exception type, message,
  stack trace, inner exceptions, application version, book schema version.
- **Log file** — where entries go, how many are kept, how large it may grow.

## Success Criteria

- **SC-001**: An exception raised on the UI thread, on a background thread, and inside a
  fire-and-forget task each produce an entry. Three tests, because there are three paths and
  two of them are unhooked today.
- **SC-002**: A log produced by a session against a real book contains **no** payee, category
  or account name and **no** amount — asserted by searching the file for values known to be in
  the book, not by reading it.
- **SC-003**: An entry contains enough to locate a fault without the machine that produced it:
  exception type, stack trace, application version.
- **SC-004**: A thousand identical failures produce a bounded log, not a thousand entries.
- **SC-005**: With the log's directory made unwritable, the application still starts, still
  opens a book, and still reports errors to the user as before.
- **SC-006**: The log file exists on disk immediately after an entry is written, without the
  process having exited cleanly.
- **SC-007**: A session that created a book records the same kind of tag as one that unlocked
  a book — never `no-book`.

## What the log found

Recorded here because it is the evidence for SC-001 and SC-003, and because a log nobody reads
is not a feature.

### 2026-09-06 — the first session

Two faults on the categories page, in the first sitting after this shipped. Neither had ever
been visible: one produced no message at all, and the other produced a sentence with nothing
behind it.

1. **`UnobservedTask` — `NotSupportedException`, a bound collection rebuilt off the interface
   thread.** The page refilled its list after awaiting background work and the resumption
   landed on a pool thread. `ConfigureAwait(true)` had been read as "come back to the
   interface thread"; it is not, and from a fire-and-forget call site — of which there are
   twenty-odd — it frequently resumes wherever the work finished. The symptom before the log
   existed was a page that silently did not refresh. Fixed by marshalling explicitly through
   `MyFinance.Core.Threading.UiContext`, and the rule added to constitution principle 9.
2. **`UnhandledOnInterfaceThread` — `InvalidOperationException`, a two-way binding onto a
   read-only property.** A `DataGridCheckBoxColumn`, which binds two way unless told
   otherwise, over a projection with no setter. The column beside it bound to a property that
   *did* have a setter and therefore threw nothing — it edited a detached entity and discarded
   the change in silence, which is the worse of the two. Fixed in
   `003-categories-and-payees` FR-022 and SC-008.

Both were fixed the same day the log named them. That is the whole argument for this feature:
the first is a fault that had produced no evidence whatsoever, and the second had produced a
message box with the type, the stack and the property name all thrown away.

The same session also showed every entry reading `book:no-book`, because the tag was recorded
only on the unlock path — FR-017 above.

## Assumptions

- Depends on constitution principles **7** (nothing leaves the machine — the log is written,
  never sent), **6** (store nothing you do not need — which is why redaction is a requirement
  and not a courtesy), **8** (real financial data must not escape into places it can be copied
  from), **9** (writing must not block the interface).
- The audience is the user and whoever they show the file to. It is not telemetry, and there is
  nobody at the other end.
- `Microsoft.Extensions.Logging` is already a dependency of `MyFinance.Data` and
  `MyFinance.Import` and is unused. Whether to build on it or write something smaller is a
  design question for the plan, not a decision here.

## Clarifications

### 2026-09-06

- **Q: Where does the log live?** → **The user's local application data**, not beside the book
  (FR-016). A book frequently sits in a synced folder; a log written there would leave the
  machine as a side effect of that. Findability is answered by FR-011 instead.
- **Q: How much may the log say about which book was open?** → **A stable hash only** (FR-009).
  Enough to tell two books' entries apart, nothing about either. Not the path — which usually
  contains the user's real name — and not the file name, which they chose and which can be as
  revealing as the contents.
- **Q: Is there a verbose mode?** → **Yes, and it adds operations rather than data** (FR-015).
  The redaction rules are absolute at every level; verbose changes how much is said about what
  the application did, never about what the book holds.
- **Q: How is the log bounded?** → **By size, with one rollover** (FR-012): two files of about
  a megabyte. Age-based retention would let a fault repeating in a loop fill a disk.

## Out of Scope

- Telemetry, crash reporting, or anything that transmits. Permanently — constitution 7.
- A log viewer inside the application. Revealing the file's location is enough.
- Logging successful operations, performance timings, or an audit trail of what the user did.
  This exists to explain failures; a general activity log is a different feature with a very
  different privacy profile.
- Changing how errors are presented to the user. See FR-005.
