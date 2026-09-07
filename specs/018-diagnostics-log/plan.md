# Implementation Plan: A diagnostics log

**Spec:** `./spec.md` · **Status:** **Built** — see `./tasks.md`

## Summary

A small append-only writer in `MyFinance.Core`, three global exception hooks in
`MyFinance.App`, and one call added to the place errors already pass through. The decision this
turns on: **the log records what the application was doing, never what the book contains** —
and that is enforced by the writer taking no free-form text from callers, rather than by
everyone remembering not to interpolate a payee name.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 7 — nothing leaves the machine | The log is written and never sent. There is no endpoint, no telemetry, no "report this" button. |
| 6 — store nothing you do not need | Redaction is the design, not a filter over it: the writer has no parameter that could carry a payee or an amount. |
| 8 — real data must not escape | A log is a plaintext file the user may well email to somebody. SC-002 searches a real log for values known to be in the book. |
| 9 — off the interface thread | Entries queue and are flushed by a background writer; a fault never waits on a disk. |
| 4 — correctness outside WPF | The writer, the redaction and the rollover are in Core and testable without Windows. Only the hooks are WPF. |

## Technical context

- **Projects:** `MyFinance.Core/Diagnostics` (the writer), `MyFinance.App` (the hooks and the
  reveal command), `MyFinance.Data` (book hash and schema version at open time).
- **Dependencies:** **none new.** `Microsoft.Extensions.Logging` is already referenced by two
  projects and unused — see the decision below.
- **Location:** `%LOCALAPPDATA%\MyFinance\logs\myfinance.log`, with one rollover to `.1`.
- **Testing:** `tests/MyFinance.Core.Tests/Diagnostics` for everything decidable;
  `tests/MyFinance.Data.Tests` for the redaction sweep against a real book.

## Design

### Not building on `Microsoft.Extensions.Logging`

It is already referenced, so using it looks free. It is not the right shape here:

- Its API is **structured message templates with interpolated arguments** —
  `LogError("Failed to save {Payee}", payee)`. That is precisely the shape this feature must
  make impossible. The redaction rule would then depend on every call site being careful, and
  59 `catch` blocks is 59 chances to be careless.
- Using it properly means a provider, a filter configuration and a scope stack; the packaging
  spec (`011`) already had to strip `Logging.Debug` and `Hosting` out of the publish for
  producing stray files, and `NFR-003` says not to reintroduce that risk.

So: a small writer whose API cannot express the dangerous case. The unused package references
should be removed by this work rather than left implying a logging story that does not exist.

### The API is the redaction policy

```csharp
DiagnosticLog.Failure(Operation.SaveTransaction, exception);
DiagnosticLog.Breadcrumb(Operation.OpenRegister);      // verbose only
```

`Operation` is an **enum**, not a string. A caller physically cannot pass a payee name, an
amount, or a file path, because there is no parameter that takes one. Redaction stops being a
rule people follow and becomes a thing the type system does.

The exception itself is the one place unbounded text arrives — a message can contain anything
a thrower put in it. `BookValidationException` messages, for instance, name payees today
(`"\"{payee}\" has nothing outstanding to enter."`). So exception **messages are scrubbed**
before writing: the type and stack are recorded in full, the message only for exception types
on a known-safe list. Everything else records the type and a note that the message was
withheld. Losing a message is a smaller cost than leaking one, and the stack trace is what
actually locates a fault.

### Three hooks, because there are three ways out

| Hook | Catches | Today |
|---|---|---|
| `DispatcherUnhandledException` | the UI thread | exists, logs nothing |
| `AppDomain.CurrentDomain.UnhandledException` | background threads | **absent** |
| `TaskScheduler.UnobservedTaskException` | the 20+ `_ = SomethingAsync()` sites | **absent** |

The third is the one most likely to explain the reported fault, because those failures produce
no message box either — the page simply does not refresh. It also needs
`e.SetObserved()` so the behaviour stays exactly as it is today (FR-005): the log is added, the
outcome is unchanged.

### Writing without becoming the failure

Entries go onto a bounded in-memory queue and a single background writer drains it. If the
queue is full, the oldest entry is dropped — a log that blocks the application to record that
the application is unwell is worse than a gap in the log.

Every write is inside a `try` that swallows: a read-only directory, a full disk or a locked
file must leave the application exactly as it was (FR-013, SC-005). The writer opens with
`FileShare.ReadWrite` and appends, so two copies of the single-file executable running at once
interleave rather than corrupt.

`NFR-002` — surviving a crash — means flushing after each entry rather than relying on process
exit. That is the one place this trades throughput for correctness, and it is the right trade:
the entry that matters most is the last one before the process died.

### Repetition

A fault in a filter handler can fire on every keystroke. Identical entries — same operation,
same exception type, same stack — are counted rather than repeated, and the count is written
when the run ends or the log rolls. Without it the log is a thousand copies of one fact and
the entry that explains it has already rolled off the end.

### Alternatives rejected

- **`Microsoft.Extensions.Logging`** — above.
- **Serilog or NLog** — a dependency and a configuration file, for a single-user desktop
  application that needs one file and no sinks.
- **Logging beside the book** — findable, and books commonly live in synced folders. That
  would send diagnostics off the machine as a side effect of where the book sits.
- **Age-based retention** — reads more naturally and lets a tight loop fill a disk.
- **Free-form message strings** — the shape every logging API offers, and the one that makes
  FR-006 a matter of discipline rather than construction.
- **A log viewer in the application** — revealing the folder is enough, and the file is plain
  text.

## Project structure

```
src/MyFinance.Core/Diagnostics/DiagnosticLog.cs      NEW — the writer, the queue, the flush
src/MyFinance.Core/Diagnostics/Operation.cs          NEW — the enum that is the whole API
src/MyFinance.Core/Diagnostics/LogEntry.cs           NEW — one entry, and how it renders
src/MyFinance.Core/Diagnostics/SafeMessages.cs       NEW — exception types safe to quote
src/MyFinance.Core/Diagnostics/BookTag.cs            NEW — the non-reversible book identifier
src/MyFinance.App/App.xaml.cs                        + two hooks; the existing one logs
src/MyFinance.App/Services/DialogService.cs          ShowError also records
src/MyFinance.App/ViewModels/ShellViewModel.cs       + a Diagnostics command that reveals it
src/MyFinance.Data/Security/BookFileService.cs       supplies the book hash on open
tests/MyFinance.Core.Tests/Diagnostics/…             NEW
tests/MyFinance.Data.Tests/Services/DiagnosticRedactionTests.cs   NEW
```

## Risks

- **The log becomes a second copy of the records.** The reason the API takes an enum and
  scrubs exception messages, and the reason SC-002 searches a real log for real values rather
  than reading it and forming an impression.
- **Logging destabilises the thing it is meant to diagnose.** Contained by the bounded queue,
  the swallowing writer, and SC-005 running the application with the log directory unwritable.
- **Verbose mode is where redaction will be tempted to slip.** It adds *which* operations ran,
  never their arguments. The same tests run at both levels so the boundary is asserted, not
  assumed.
- **This makes the next occurrence diagnosable, not the last one.** Nothing recovers the
  exceptions already hit. Worth saying plainly to whoever asked for it.

## What changed during implementation

1. **The swallow list was too narrow, and a test caught it.** `Append` caught `IOException`,
   `UnauthorizedAccessException` and `NotSupportedException` — but `Directory.CreateDirectory`
   throws `ArgumentException` on a malformed path, before any of those can be reached, and
   `An_unwritable_directory_is_survived_silently` failed on it. FR-013 says a logging failure
   must never surface; the guard is now a single `IsWriteFailure` covering everything that can
   go wrong reaching a file. This is the one place in the solution where swallowing broadly is
   the right thing.
2. **Verbose had to be switchable while running.** The plan read it from options at
   construction, which would have made the toggle mean "restart and lose the state that
   provokes the fault". It is a live property now.
3. **`ShowError` cannot log its message.** FR-002 asked for an entry when a caught failure is
   reported to the user, and the obvious implementation logs the message — which is precisely
   the free text that carries payees. What is recorded is the *fact and the timestamp*, so "it
   broke this morning" lines up with the breadcrumb saying what was running. Less than hoped
   for, and the alternative was a leak.
4. **A path scrubber was needed as well as a message allow-list.** Even a safe type leaks:
   `IOException` names the file it failed on, and that file is usually the book. Stack traces
   carry build paths too. Everything written goes through `PathScrubber` regardless of type.
5. **Repeats collapse on powers of two**, not on first sight. Silencing a repeating fault after
   the first entry loses the fact that it is *still happening*; a handful of lines saying "×2,
   ×4, ×8" is both bounded and informative.

