# Feature Specification: Printing

**Folder:** `016-printing`
**Created:** 2026-09-05
**Status:** **Implemented** (2026-09-05)
**Input:** Identified while retro-specifying the codebase — there is no printing path anywhere
in it.

## Why this is a gap

Microsoft Money printed registers, reports and cheques. This application prints **nothing**:
there is no `PrintDialog`, no document paginator, no print command on any screen.

For reports, CSV export plus a spreadsheet is a reasonable answer, and it is the answer today
(`008-reports-and-dashboard`, FR-028). It is not an answer for someone who wants a register
on paper — to check against a statement at a table, to keep with tax records, or to hand to
an accountant. "Open it in Excel and format it yourself" is a worse experience than the
application this one replaces.

## User Scenarios & Testing

### Primary user story

The user wants a clean printed page: a register for a date range, or a report as it appears on
screen, with a heading saying which account and which period, page numbers, and figures that
line up.

### Acceptance scenarios

1. **Given** a register with a date filter, **When** the user prints, **Then** the filtered
   rows are printed with a heading naming the account and period, and page numbers.
2. **Given** a report, **When** the user prints, **Then** both the chart and the table appear,
   as they do on screen.
3. **Given** a long register, **When** it is printed, **Then** it paginates with column
   headings repeated on each page.
4. **Given** a preview, **When** the user changes orientation or paper size, **Then** the
   preview updates before anything is printed.

### Edge cases

- A register wider than the page — **the user chooses which columns print**, from a sensible
  default. Nothing is silently dropped: a printout that disagrees with the screen is the
  failure this feature exists to avoid, and a missing memo column the user did not ask for is
  exactly that.
- Printing to PDF, which on Windows is a printer and may need nothing special.
- A chart's colours on a monochrome printer — the palette is validated for contrast
  (`008-reports-and-dashboard`, NFR-003), but a printed bar chart in greyscale needs texture
  or direct labels, not hue.

## Requirements

### Functional requirements

- **FR-001**: The system MUST print a register for the current filter, with a heading naming
  the account and the period.
- **FR-002**: The system MUST print a report, chart and table together.
- **FR-003**: The system MUST paginate, repeating column headings and numbering pages.
- **FR-004**: The system MUST offer a preview before printing.
- **FR-005**: Printed output MUST NOT rely on colour alone to carry meaning.
- **FR-006**: The system MUST let the user choose which columns are printed, defaulting to a
  set that fits the page, and MUST NOT drop a column silently.

### Non-functional requirements

- **NFR-001**: Printing MUST NOT block the interface thread (constitution 9).
- **NFR-002**: The printed figures MUST come from the same computed values as the screen —
  never a second formatting path that could disagree.

## Key Entities

**None.** Printing persists nothing and consumes the shapes `002-accounts-and-register` and
`008-reports-and-dashboard` already produce, which is why there is no `data-model.md` here.
The only state is a remembered column selection and page setup, held as preferences through
the existing `SettingsService`.

## Success Criteria

- **SC-001**: A printed register's rows and totals match the screen exactly.
- **SC-002**: A multi-page register repeats its column headings and numbers its pages.
- **SC-003**: A printed report is readable in greyscale.

## Assumptions

- Depends on constitution principle **4** — and is in tension with it: printing is WPF-only
  and can only be verified by looking at output, so the *figures* must come from Core and only
  the layout can live in the view layer.
- The whole feature requires Windows to test, which is why it has stayed unbuilt.

## Out of Scope

- Emailing or sharing a printout. Constitution 7.
- **Cheque printing.** Money did it; it needs pre-printed stock, an alignment calibration
  screen and MICR considerations, and it is a different feature wearing the same word. Few
  people write cheques now, and none of the rest of this feature depends on it.

## Clarifications

### 2026-09-05

- **Q: Is cheque printing in scope?** → **No.** Registers and reports only. Cheque printing
  needs pre-printed stock, alignment calibration and a test-print flow; it is a separate
  feature that happens to share a verb.
- **Q: When a register is wider than the page, which columns may be dropped and who decides?**
  → **The user decides**, from a default that fits (FR-006). Nothing is dropped silently.
  *(Assumed from NFR-002 — the printed figures must agree with the screen — rather than
  asked.)*

