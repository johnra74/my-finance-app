# Tasks: A diagnostics log

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** **complete**

New coverage: `tests/MyFinance.Core.Tests/Diagnostics/` (36) and
`tests/MyFinance.Data.Tests/Services/DiagnosticRedactionTests.cs` (8) — **44 tests**. The suite
went from 1,051 to **1,095**.

Five things differed from the plan and are recorded at the end of `plan.md`; the first is a
real bug a test caught — the writer's swallow list missed the exception
`Directory.CreateDirectory` actually throws.

`[ ]` outstanding · `[P]` may run in parallel with its neighbours.
Every task names the file it creates or changes, the requirement it satisfies, and the test
that will prove it. Those tests do not exist yet; each says where it will live and what it
will assert.

**The ordering matters here.** The writer and its redaction come first, and the hooks that
feed it come second — so there is never a point where exceptions are being captured by
something whose output has not been proved safe to keep.

## Phase 1 — The writer, and the redaction that is built into it

- [X] **T001** `Operation`: the enum that is the entire caller-facing vocabulary
  - Implements: `src/MyFinance.Core/Diagnostics/Operation.cs`
  - One value per thing that can fail and matter — opening a book, saving a transaction,
    importing, migrating, backing up, exporting, running a report, refreshing a page.
  - Proven by: `DiagnosticLogTests.The_api_offers_no_way_to_pass_free_text` — a reflection test
    over `Failure` and `Breadcrumb` asserting no `string` parameter exists. Redaction is
    something the compiler enforces, not something 59 catch blocks remember.
  - Requirements: FR-006, FR-008
- [X] **T002** `LogEntry` and its rendering: timestamp, severity, operation, exception type,
  stack, inner exceptions, app version, book schema version, book hash
  - Implements: `src/MyFinance.Core/Diagnostics/LogEntry.cs`
  - Proven by: `LogEntryTests.An_entry_carries_the_type_and_stack_not_just_the_message`,
    `An_entry_names_the_application_version`, `Inner_exceptions_are_recorded_to_the_root`,
    `The_rendered_line_is_culture_independent`
  - Requirements: FR-003, FR-004
- [X] **T003** ⚠️ Message scrubbing: quote an exception's message only for types known not to
  carry user data; otherwise record the type and say the message was withheld
  - Implements: `src/MyFinance.Core/Diagnostics/SafeMessages.cs`
  - `BookValidationException` messages name payees today — *"\"{payee}\" has nothing
    outstanding to enter."* — so this is not hypothetical.
  - Implements: `src/MyFinance.Core/Diagnostics/SafeMessages.cs`, and `PathScrubber` alongside
    it — **not in the plan**: even a safe type leaks, because `IOException` names the file it
    failed on and that file is usually the book. Stack traces carry build paths too, so
    everything written is scrubbed regardless of type.
  - Proven by: `SafeMessageTests.A_validation_message_is_withheld_because_it_can_name_a_payee`,
    `A_framework_exception_message_is_kept`,
    `A_withheld_message_still_records_the_exception_type`,
    `An_unknown_type_is_withheld_by_default` (an allow-list, so the mistake falls safe),
    `A_path_is_removed_even_from_a_safe_message`, and `PathScrubberTests`
    (`Ordinary_prose_survives_untouched` — "and/or" must not read as a path)
  - Losing a message costs less than leaking one, and the stack is what locates a fault.
  - Requirements: FR-006, FR-007
- [X] **T004** The book tag: stable per book, non-reversible, recoverable from neither the
  name nor the path
  - Implements: `src/MyFinance.Core/Diagnostics/BookTag.cs`
  - Its own type rather than a method on the writer: it has one job, and it is the one piece of
    the entry that touches something sensitive.
  - Proven by: `BookTagTests.The_same_book_tags_the_same_way_twice`,
    `Two_books_tag_differently`, `The_tag_reveals_neither_the_name_nor_the_path`,
    `The_same_book_tags_the_same_way_whatever_the_casing`,
    `The_same_name_in_two_folders_tags_differently`
  - Requirement: FR-009
- [X] **T005** The writer: bounded queue, background flush, append with sharing, flush per
  entry, swallow every failure
  - Implements: `src/MyFinance.Core/Diagnostics/DiagnosticLog.cs`
  - Proven by: `DiagnosticLogTests.An_entry_reaches_the_file`,
    `A_full_queue_drops_the_oldest_rather_than_blocking`,
    `An_unwritable_directory_is_survived_silently`,
    `Two_writers_append_without_corrupting_each_other`,
    `An_entry_is_on_disk_without_a_clean_shutdown`,
    `A_null_exception_is_ignored_rather_than_throwing`
  - ⚠️ `An_unwritable_directory_is_survived_silently` **failed first time and was right to**:
    the swallow list missed `ArgumentException`, which `Directory.CreateDirectory` throws
    before any of the caught types can be reached. See `plan.md` note 1.
  - Requirements: FR-013, NFR-001, NFR-002
- [X] **T006** Size-bounded rollover: two files of about a megabyte, oldest entries first
  - Implements: `DiagnosticLog`
  - Proven by: `DiagnosticLogTests.The_log_rolls_over_at_the_size_cap`,
    `Only_two_files_are_ever_kept`
  - Those tests grow the file with **breadcrumbs**, not failures: failures raised from one call
    site share a stack and are collapsed on purpose, so they cannot grow a file. Worth knowing
    before writing another test here.
  - Requirement: FR-012
- [X] **T007** Repetition collapsing: identical operation, type and stack are counted, not
  repeated
  - Implements: `DiagnosticLog`
  - A fault in a filter handler fires on every keystroke; without this the entry that explains
    it has already rolled off the end.
  - Collapses on **powers of two** rather than after the first: going silent would lose the
    fact that a fault is *still happening*, and "×2, ×4, ×8" is both bounded and informative.
  - Proven by: `DiagnosticLogTests.A_thousand_identical_failures_produce_one_counted_entry`,
    `A_different_stack_is_not_collapsed_into_the_same_entry`
  - Requirement: FR-014
- [X] **T008** [P] Verbose mode: breadcrumbs of which operations ran, off by default
  - Implements: `DiagnosticLog`, and the setting through
    `src/MyFinance.Data/Services/SettingsService.cs` (`diagnostics.verbose`)
  - Live rather than read once at construction — **changed from the plan**, which would have
    made the toggle mean "restart, and lose the state that provokes the fault".
  - Proven by: `DiagnosticLogTests.Breadcrumbs_are_silent_until_verbose_is_on`,
    `Verbose_adds_operations_and_no_arguments`
  - Requirement: FR-015

## Phase 2 — Proving it is safe to keep before anything fills it

- [X] **T009** ⚠️ **The redaction sweep.** Run a real book through a session that fails, then
  search the log for values known to be in that book
  - Implements: `tests/MyFinance.Data.Tests/Services/DiagnosticRedactionTests.cs`
  - Proven by: `DiagnosticRedactionTests.No_payee_category_or_account_name_reaches_the_log`,
    `No_amount_reaches_the_log`, `No_book_path_or_file_name_reaches_the_log`, and
    `The_log_still_says_enough_to_locate_the_fault` — redaction that removed everything useful
    would be its own kind of failure.
  - Each is a `[Theory]` over both verbosity levels, so **eight** cases in total. The session
    provokes four real service failures, including ones whose messages name a payee and a
    category outright.
  - Asserted by **searching the file for strings taken from the book**, not by reading it and
    forming an impression. This is the test that makes the feature safe to ship, and it runs
    at both levels.
  - Requirements: FR-006, FR-007, FR-009, FR-015, SC-002

## Phase 3 — The three ways an exception escapes

- [X] **T010** The existing UI-thread handler also logs
  - Implements: `src/MyFinance.App/App.xaml.cs` (`OnDispatcherUnhandledException`)
  - The message box and `Handled = true` stay exactly as they are — the log is added beside
    them, not instead of them.
  - Logged **before** the box is shown: if showing it fails too, the original fault is already
    on disk.
  - Proven by: **needs Windows** for the box; the writer by Phase 1.
  - Requirements: FR-001, FR-005
- [X] **T011** `AppDomain.CurrentDomain.UnhandledException` — background threads
  - Implements: `src/MyFinance.App/App.xaml.cs`
  - Absent today: such a failure kills the process with nothing recorded.
  - Proven by: **needs Windows.**
  - Requirement: FR-001
- [X] **T012** ⚠️ `TaskScheduler.UnobservedTaskException` — the 20+ `_ = SomethingAsync()` sites
  - Implements: `src/MyFinance.App/App.xaml.cs`
  - **The one most likely to explain the reported fault.** Those failures produce no message
    box either; the page simply does not refresh. Calls `e.SetObserved()` so the outcome stays
    exactly as it is today.
  - Calls `e.SetObserved()`, so the outcome is exactly what it is today and only the log is
    added.
  - Proven by: **needs Windows** for the hook itself; the writer by Phase 1.
  - Requirements: FR-001, FR-005
- [X] **T013** `ShowError` also records, so a message the user saw has a counterpart
  - Implements: `src/MyFinance.App/Services/DialogService.cs`,
    `src/MyFinance.App/Services/DiagnosticSink.cs` (`UserFacingError`)
  - ⚠️ **Less than the requirement hoped for.** The message cannot be logged — it is free text
    built from an exception, and in this application that routinely means a payee. What is
    recorded is the *fact and the timestamp*, which lets "it broke this morning" line up with
    the breadcrumb saying what was running. 22 call sites go through it; none needed editing.
  - Proven by: **needs Windows.**
  - Requirement: FR-002

## Phase 4 — Finding it

- [X] **T014** A **Diagnostics** entry in the top bar that reveals the log folder, and a
  verbose toggle beside it
  - Implements: `src/MyFinance.App/ViewModels/ShellViewModel.cs`,
    `src/MyFinance.App/Views/ShellWindow.xaml`
  - Implements: `ShellViewModel.Diagnostics`, the **Diagnostics** button in `ShellWindow.xaml`,
    and `src/MyFinance.App/Services/DiagnosticLogFactory.cs` (the `%LOCALAPPDATA%` location).
    Reuses `IDialogService.OpenFolder`, which already exists for backups.
  - The dialog says what the log does and does not record before offering the toggle — the
    user should not have to take that on trust.
  - Proven by: **needs Windows.**
  - Requirements: FR-011, FR-015, FR-016
- [X] **T015** [P] Remove the two unused `Microsoft.Extensions.Logging` references
  - Implements: `src/MyFinance.Data/MyFinance.Data.csproj`,
    `src/MyFinance.Import/MyFinance.Import.csproj`, `Directory.Packages.props`
  - They are referenced and never called, which implies a logging story that does not exist —
    and `011` already had to strip logging packages out of the publish for producing stray
    files.
  - Proven by: the solution building with zero warnings, and `./build.sh publish` still
    producing **one 85 MB file with no strays** — the property `011` had to fight for.
  - Requirement: NFR-003

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T010, T011, T012 | ✅ writer tested in Core; ⚠️ **the three hooks need Windows** |
| FR-002 | T013 | ⚠️ **the fact and the timestamp only — the message cannot be kept** |
| FR-003, FR-004 | T002 | ✅ |
| FR-005 | T010, T012 | ✅ `SetObserved`, and the box unchanged; ⚠️ visible behaviour needs Windows |
| FR-006, FR-007 | T001, T003, T009 | ✅ **the ones that make it safe to ship** |
| FR-008 | T001 | ✅ |
| FR-009 | T004, T009 | ✅ |
| FR-010 | By construction: the writer opens a file and nothing else | ✅ |
| FR-011 | T014 | ⚠️ needs Windows |
| FR-012 | T006 | ✅ |
| FR-013 | T005 | ✅ — and the test earned its keep |
| FR-014 | T007 | ✅ |
| FR-015 | T008, T009, T014 | ✅ logic; ⚠️ the toggle needs Windows |
| FR-016 | T014 | ✅ |
| NFR-001, NFR-002 | T005 | ✅ |
| NFR-003 | T015 | ✅ publish still one file, no strays |
| SC-002 | T009 | ✅ **searched, not read** |

## What this will not do

- **Recover the exceptions already hit.** Those are gone. This makes the next occurrence
  diagnosable.
- **Explain a fault on its own.** It records what failed and where; whether that is enough
  depends on the fault. Verbose mode exists because for an intermittent one it often is not.
- **Be verifiable end to end without Windows.** The writer, the redaction and the rollover are
  all testable here; the three hooks, the Diagnostics button and the toggle are not. That is
  the largest remaining risk: the hooks are four lines each, but nothing here proves they fire.
