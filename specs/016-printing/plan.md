# Implementation Plan: Printing

**Spec:** `./spec.md` · **Status:** **Built** — see `./tasks.md`

## Summary

Pagination arithmetic and column selection are pure functions in `MyFinance.Core`; the WPF
layer turns their output into a `FlowDocument` and hands it to the platform's print stack. The
decision this turns on: **the printed figures come from the same computed values as the
screen** — printing renders what is already there, and never recomputes or reformats anything.

This feature persists nothing beyond a remembered column selection, so there is no
`data-model.md`.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 4 — correctness outside WPF | **In tension, and resolved deliberately.** Printing can only be *seen* on Windows, so everything decidable — how many rows fit, where a page breaks, which columns are chosen — is computed in Core and tested there. Only the drawing is left to the layer that can be checked by eye alone. |
| 10 — the user's data stays theirs | Paper is a form of portability, and the one that outlives every file format. |
| 9 — off the interface thread | Building a long document and spooling it both run through `RunBusyAsync`. |
| 7 — nothing leaves the machine | A local printer, or the platform's print-to-PDF. No sharing affordance. |

## Technical context

- **Projects:** `MyFinance.Core/Printing` (new — pagination and column fitting),
  `MyFinance.App/Printing` (new — documents and the print dialog).
- **Dependencies:** none new. WPF's `FlowDocument`, `DocumentPaginator` and `PrintDialog` are
  in the framework.
- **Existing pieces reused rather than rebuilt:**
  - `BalanceCalculator` output and `RegisterPageViewModel`'s rows — the register printout is
    the rows already on screen under the current filter.
  - `ReportEngine` / `ChartGeometry` output — the report printout is the shapes already
    computed. **The chart is redrawn at print resolution from the same geometry**, not
    screen-captured.
  - `SettingsService` for the remembered column selection and page setup.
  - `BusyViewModel.RunBusyAsync` and `WorkProgress`.
- **Testing:** `tests/MyFinance.Core.Tests/Printing/` for everything decidable. The rest needs
  a person and a printer, and the plan says so rather than pretending otherwise.

## Design

### What is testable here, and what genuinely is not

This is the one feature in the codebase whose output cannot be verified in CI, so the split
has to be deliberate:

| Tested in `MyFinance.Core.Tests` | Needs Windows and a person |
|---|---|
| How many rows fit a page at a given height | That the text is legible |
| Where page breaks fall, and that headings repeat | That columns line up |
| Which columns are chosen for a given page width | That the printer receives it |
| Page numbering and the "page *n* of *m*" total | That greyscale output reads |
| That the row set printed equals the row set filtered | Paper size and orientation handling |

If pagination were done by handing a `FlowDocument` to WPF and letting it break where it
likes, **the entire left-hand column would move to the right-hand one**. That is the reason
pagination is computed rather than delegated.

### The printed figures are the screen's figures

`NFR-002` is load-bearing. The printout takes the already-computed rows — the same `Money`
values, the same formatting helpers — and lays them out. It does not re-query, re-aggregate or
re-format. A second formatting path would eventually disagree with the first, and a printout
that disagrees with the screen is worse than no printout: it is the copy the user takes to
their accountant.

### Columns the user chooses

A register has more columns than a portrait page holds. The default is a set that fits; the
user can change it and the choice is remembered. Nothing is dropped silently — a memo column
that vanished without being asked to is exactly the "disagrees with the screen" failure, and
the user has no way to notice it.

### Charts are redrawn, not captured

A report prints its chart from `ChartGeometry` at print resolution. Screen-capturing the
on-screen control would produce a blurred bitmap at 96 dpi on a 600 dpi device. Since the
geometry is already a pure function, redrawing costs a second renderer and no new arithmetic.

### Greyscale

`FR-005` restates constitution-adjacent policy from `008-reports-and-dashboard`: colour never
carries meaning alone. In print that matters more, because a colour printer is not a given.
The table beside every chart already covers this; direct labels on the chart close the rest.

### Alternatives rejected

- **Letting WPF paginate a `FlowDocument`.** Convenient, and it moves every checkable property
  into the layer that cannot be checked.
- **Screen-capturing the chart.** Blurred at print resolution.
- **Printing via an HTML or PDF library.** A large dependency in a single-file build, to
  reimplement what the platform already does well.
- **"Just export to CSV and print from a spreadsheet."** This is today's answer, and it is why
  the gap exists: it is not an answer for a register somebody wants on paper.
- **Cheque printing.** Out of scope, per the clarification.

## Project structure

```
src/MyFinance.Core/Printing/PageLayout.cs        NEW — rows per page, breaks, numbering
src/MyFinance.Core/Printing/ColumnFitter.cs      NEW — which columns fit a given width
src/MyFinance.App/Printing/RegisterDocument.cs   NEW — FlowDocument for a register
src/MyFinance.App/Printing/ReportDocument.cs     NEW — FlowDocument for a report + chart
src/MyFinance.App/Printing/PrintCommand.cs       NEW — preview, page setup, PrintDialog
src/MyFinance.Data/Services/SettingsService.cs   the print.columns / print.pagesetup keys
tests/MyFinance.Core.Tests/Printing/PageLayoutTests.cs     NEW
tests/MyFinance.Core.Tests/Printing/ColumnFitterTests.cs   NEW
```

## Risks

- **Unverifiable in CI.** The mitigation is the table above: move everything decidable out of
  WPF, and be explicit in the tasks about which items need a person. The risk is not that this
  breaks — it is that it *looks* covered when it is not, which the coverage table must not
  allow.
- **A second formatting path creeping in.** The likely shape of the bug: a print-specific date
  or amount format that drifts from the screen's. Contained by the printout consuming the
  already-formatted view models rather than the entities.
- **Long registers.** A twenty-five-year register is thousands of pages, and someone will
  print one by accident. The preview must state the page count before anything spools, and the
  operation must be cancellable.

## What changed during implementation

1. **A `FixedDocument`, not a paginated `FlowDocument`.** The plan said flow documents; that
   was inconsistent with its own central decision. Since `PageLayout` already works out how
   many rows fit and where the breaks fall, a flow document would have had WPF paginate a
   second time — and the whole point was to keep that arithmetic where it can be tested. One
   `FixedPage` per computed page is the shape that follows from the decision actually made.
2. **The chart prints on its own sheet**, not above the table on page one. Composing it inline
   would have given page one a different row capacity from every other page — a special case
   whose only merit is saving one sheet of paper.
3. **`DateText` moved out of the XAML.** The register grid formatted its date with
   `StringFormat=d` while money already went through shared `…Text` properties. NFR-002 forbids
   a second formatting path, so the date now has a single one too, and the grid binds to it.
   Small, and exactly the drift the requirement exists to prevent.
4. **Bars print in one ink, not the screen's palette.** On paper the series are identified by
   the label and figure printed beside each bar. A printed rainbow would only invite the reader
   to match slices against a legend that greyscale has already flattened — which is FR-005
   taken seriously rather than restated.
5. **Spooling goes through `XpsDocumentWriter.WriteAsync`**, not `PrintDialog.PrintDocument`.
   Worth being precise about what that buys: composing pages still happens on the UI thread,
   because WPF elements can only be built where they live. What it avoids is the window
   freezing while a long job is queued. NFR-001 is met for spooling, not for composition, and
   the task list says so rather than claiming more.

