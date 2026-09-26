# Feature Specification: Inline transaction entry at the bottom of the register

**Folder:** `020-register-entry-panel`
**Created:** 2026-09-20 (written before implementation)
**Status:** Built
**Input:** "Replace the modal transaction entry form with an inline panel at the bottom of
the register, docked below the grid, toggled on demand, staying open across consecutive
entries, switching between payment, deposit and transfer modes."

> A spec says **what** and **why**. No technology, no file names, no API shapes — those
> belong in `plan.md`. Anything genuinely undecided is marked `[NEEDS CLARIFICATION: the
> question]` rather than guessed at.

## User Scenarios & Testing

### Primary user story

Someone working through a bank statement or catching up bills wants to enter and edit many
transactions in one sitting — payment after deposit after transfer — without a modal dialog
appearing and disappearing for each one, breaking their view and their flow. They enter a
transaction, it saves and the panel clears for the next entry, or they select an existing row
to edit it inline. The whole capture stays keyboard-driven: the register is never left, and
mode switching and navigation stay in reach.

### Acceptance scenarios

1. **Given** a register page, **When** the user presses Ctrl+N or clicks "New", **Then** the
   entry panel appears at the bottom of the register in payment mode, focused and ready.
2. **Given** the entry panel is closed, **When** the user double-clicks a register row or
   selects it and presses Enter, **Then** the panel opens in the mode matching the row's type
   (payment, deposit, or transfer) with its fields pre-filled.
3. **Given** a new transaction in the panel with a valid amount, date and category, **When**
   the user presses Enter or clicks "Save", **Then** it saves without closing the panel, the
   fields clear for the next entry, and focus returns to the panel to accept the next payee
   name.
4. **Given** an edited transaction in the panel, **When** the user presses Enter or clicks
   "Save", **Then** the register row updates in place and the panel remains open.
5. **Given** the panel open in payment or deposit mode, **When** the user presses Tab or
   clicks the transfer-mode button, **Then** the category field is hidden, a target-account
   selector appears, and the mode is marked.
6. **Given** a transfer mode with a target account selected, **When** the user changes the
   target account, **Then** the matching mode stays active and the far leg's target is ready
   to be verified on save.
7. **Given** a transaction split across multiple categories, **When** the user clicks "Split…"
   or presses a keyboard shortcut in the panel, **Then** a secondary interface appears to add
   and adjust split lines, and on closing returns a summary (e.g. "Split across 3 categories")
   to the panel.
8. **Given** a new transaction with a payee name that has been used before, **When** the payee
   loses focus, **Then** the panel offers to pre-fill the amount and category from that
   payee's history, or auto-fills with a category suggestion from the classifier.
9. **Given** unsaved changes in the entry panel, **When** the user presses Escape or clicks
   "Cancel", **Then** all unsaved edits are discarded and the panel remains open and ready
   for the next entry.
10. **Given** the entry panel open with unsaved changes, **When** the user clicks a different
    register row, **Then** a prompt appears asking to save, discard, or cancel; "cancel" keeps
    the original row selected and the panel unchanged.

### Edge cases

- Selecting a register row while unsaved edits are pending in the panel.
- Switching entry mode (payment ↔ deposit) mid-entry without losing the entered data.
- A transaction already open for edit is deleted by an external event.
- Attempting to edit a transaction in a balance-only (unsupported) account.
- Attempting to edit a reconciled transaction (the panel may show a warning).
- A zero-amount entry (allowed only for transfers between accounts? or allowed always?).
- Entering a date outside the valid range (before 1900) — validation error appears.
- The payee field receives a name that does not yet exist — it is created on save.

## Requirements

### Functional requirements

**Showing and hiding**

- **FR-001**: The entry panel MUST be hidden by default and appear when the user initiates a
  new entry or selects a row to edit.
- **FR-002**: The entry panel MUST remain visible after a new transaction is saved, ready for
  the next entry without requiring a separate "New" action.
- **FR-003**: The entry panel MUST be dismissible by a "Cancel" button or keyboard (Escape),
  discarding unsaved changes.
- **FR-004**: The panel's visibility state MUST be persisted across sessions, app-wide (not
  per account), via the user's preferences.

**Entry modes**

- **FR-005**: The entry panel MUST offer three distinct modes: payment, deposit, and transfer.
- **FR-006**: In payment and deposit modes, the panel MUST show payee, category, memo, number,
  and amount fields, accepting the direction (payment = money leaving; deposit = money
  entering) as implicit in the chosen mode (per **spec 002 FR-018**: amount as magnitude plus
  direction, not signed).
- **FR-007**: In transfer mode, the panel MUST hide the category field, show a target-account
  selector, and otherwise follow **spec 002 FR-026–029**: a transfer is two linked rows that
  cancel; same-account transfer is refused.
- **FR-008**: The panel MUST label the amount field clearly to match the mode (e.g., "Payment"
  vs "Deposit"), eliminating ambiguity about sign.

**New vs edit**

- **FR-009**: When starting a new entry, the panel MUST clear all fields, move focus to the
  payee field, and mark the transaction as unsaved.
- **FR-010**: When editing a register row, the panel MUST populate all fields from that
  transaction and pre-select its mode.
- **FR-011**: When editing and saving, the panel MUST update the register row in place
  (following **spec 002 FR-021**: replacing splits rather than accumulating).

**Splits**

- **FR-012**: The panel MUST show a "Split" button or keyboard shortcut to open a split
  editor (secondary interface, specifics deferred per below).
- **FR-013**: When a transaction has multiple splits, the panel MUST show a summary (e.g.
  "Split across 3 categories") in place of a single category name (following **spec 002
  FR-019**: every transaction has at least one split).
- **FR-014**: Splits MUST sum to the transaction total; a save attempt with unbalanced splits
  MUST be refused (following **spec 002 FR-020**).

**Payee memory**

- **FR-015**: The payee field MUST be an editable, autocompleting field over existing payee
  names, creating a new payee on save if the name is not found.
- **FR-016**: When a payee that has been used before loses focus, the panel MUST offer to
  pre-fill the amount and/or category from that payee's history (following **spec 002
  FR-022–023**: reuse the payee, remember its last amount/category, never teach a single
  category from a split).

**Validation**

- **FR-017**: The panel MUST validate the transaction and report every validation failure
  together (following **spec 002 FR-024–025**: reject dates before 1900; report all errors
  at once).
- **FR-018**: The panel MUST refuse a save to a balance-only (unsupported) account (following
  **spec 002 FR-034**).
- **FR-019**: All validation logic and amount parsing MUST remain testable outside WPF
  (following constitution principle 4: correctness lives outside the WPF layer).

**Keyboard and accessibility**

- **FR-020**: Every control in the panel MUST be reachable by Tab key, and the panel MUST
  respect keyboard focus order sensibly (payee → amount → category → splits → save/cancel).
- **FR-021**: Enter (default action) MUST save; Escape MUST cancel.
- **FR-022**: Keyboard shortcuts (e.g. Ctrl+N for new, Ctrl+S for save) MUST work from the
  register view without requiring the panel to have focus.

**Cancellation**

- **FR-023**: Pressing Escape or clicking "Cancel" MUST discard unsaved changes and leave the
  panel open for a fresh entry.
- **FR-024**: When the user selects a different register row with unsaved panel changes, the
  system MUST prompt (save, discard, or cancel); if the user cancels, the original row and
  panel state remain unchanged (same as Acceptance scenario 10 above).

**Split editor**

- **FR-025**: The split editor MUST remain a separate modal dialog, opened via the same
  mechanism as today, unchanged.

**Balance-only and reconciled transactions**

- **FR-026**: The panel MUST refuse to edit a transaction in a balance-only account
  (following **spec 002 FR-034**).
- **FR-027**: When editing a reconciled transaction, the panel MUST show a warning and require
  confirmation to unreconcile.

### Non-functional requirements

- **NFR-001**: Saving and validating a transaction in the panel MUST remain responsive (under
  100 ms) for a book of ~20,000 transactions.
- **NFR-002**: The panel MUST not block the UI thread during save; async work MUST marshal
  back to the UI thread correctly (following constitution principle 9: long work is off the
  UI thread, cancellable, and transactional).
- **NFR-003**: Entering a new transaction without closing the panel MUST not degrade
  performance or leak memory after many consecutive entries.

## Key Entities

This feature does not introduce new data entities. It changes the *capture* of the
transactions, amounts, splits, payees and transfers already owned and modelled by **spec 002
(Accounts and the register)**, shifting them from a modal dialog to an inline panel docked to
the register. All entity definitions, invariants, and relationships remain unchanged.

## Success Criteria

- **SC-001**: A user can enter ten transactions in sequence without closing and reopening any
  dialog or panel between entries, only pressing Tab/Enter and typing.
- **SC-002**: Every acceptance scenario in **spec 002** remains true: amounts are captured and
  validated as magnitude + direction; splits are created, sum-checked, and saved; transfers
  are two linked rows in step; payees are reused and their history remembered.
- **SC-003**: A keyboard-only pass — no mouse — can successfully add a new transaction, edit
  an existing one, cancel unsaved changes, and navigate between register rows and the entry
  panel.
- **SC-004**: Editing a transaction in the register and saving it returns the register to its
  consistent state without requiring a refresh or re-opening the register (balance remains
  correct, running balance updates).
- **SC-005**: Validation failures are reported together in the panel without dismissing it
  (e.g., "Date before 1900. No category selected.").

## Assumptions

- Depends on constitution principles: **1** (Money is integer cents — amount parsing and
  arithmetic stay exact), **2** (amounts signed from owning account's POV; transfer as two
  rows — preserved by the panel's FR-006–007), **3** (every write through a service in
  `MyFinance.Data/Services` — the panel calls `RegisterService`, never `DbContext`
  directly), **4** (correctness lives outside WPF — validation, splits, transfers,
  category suggestion, amount parsing stay in `MyFinance.Core` and are testable outside
  WPF), **9** (long work off the UI thread — async save is cancellable and transactional,
  marshals back via explicit context).
- The entry panel is a *presentation-layer feature* that does not change the semantics or
  invariants defined in **spec 002 (Accounts and the register, FR-018–034)**. All
  functional requirements below are stated as *preserving* those FRs, not redefining them.
- A "balance-only" account remains read-only (per spec 002), and the panel enforces this.
- Reconciliation state (cleared vs. reconciled) is not directly editable in the panel; it may
  be shown read-only or with a warning if the user attempts to edit a reconciled row.

## Out of Scope

- Redesigning the split editor itself (secondary interface specifics, visual layout,
  performance on >10 splits). The split editor's own capability and invariants are out of
  scope; this feature only changes where / how the split-entry step is initiated.
- Changing category suggestion logic or the payee-memory algorithm (**spec 005**).
- Bulk or CSV transaction entry; this spec covers single-transaction entry only.
- Changes to reconciliation workflow or UI (**spec 010**).
- Investment transactions, loan amortization, or multi-currency arithmetic (those are covered
  by **specs 013, 015** respectively).

## Clarifications

### 2026-09-20

- **Q: MUST the panel's visibility state be remembered (per account, or app-wide)?** (FR-004)
  → **App-wide, one setting**, persisted in the user's preferences via `SettingsService` (same
  pattern as `016-printing`'s remembered column selection). Scope is app-wide rather than
  per-account to match the existing precedent and because a panel is a global UI affordance,
  not an account-specific editor window.
  *(Assumed, for consistency with the only existing precedent for a remembered UI preference
  in this codebase.)*
- **Q: When the user selects a different register row with unsaved changes pending, what
  happens?** (Acceptance scenario 10, FR-024) → **Prompt the user: save, discard, or
  cancel**; if the user cancels, the original row selection and panel state are restored
  unchanged. *(Assumed, matching the existing confirm-before-destructive-action precedent of
  `RegisterPageViewModel.DeleteTransactionCommand`.)*
- **Q: MUST the split editor stay a secondary dialog/window, or be redesigned inline?**
  (FR-025) → **Stays a separate modal**, opened identically to today via `IModalService` and
  `SplitEditorViewModel`. No redesign in scope; splitting logic is orthogonal to the
  panel-vs-modal presentation choice. *(Assumed; confirmed architecturally conflict-free.*)
