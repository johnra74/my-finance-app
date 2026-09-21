# Implementation Plan: Inline transaction entry at the bottom of the register

**Spec:** `./spec.md` · **Status:** Draft (unbuilt)

## Summary

The modal transaction-entry dialog (`TransactionEditorViewModel` / `TransactionEditorWindow`)
is lifted into an inline, docked `UserControl` panel that lives at the bottom of the register
page and stays open across entries, switching between payment, deposit, and transfer modes.

The one genuinely new piece of logic — mapping an entry mode (payment/deposit/transfer) to a
signed `Money` amount and normalized category/transfer fields — is extracted into
`MyFinance.Core` as `EntryModeMapper` so it is actually testable in isolation, honoring
**Principle 4** (correctness lives outside WPF). Everything else reuses the already-proven
core logic: `RegisterService.SaveAsync`, `TransactionValidator`, `SuggestionService`, payee
memory, split management (via the existing modal `SplitEditorViewModel`).

The modal is retired entirely once panel parity is confirmed: no fallback, no parallel path.
An invariant enforced in only some write paths is not an invariant.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| **1 — Money is integer cents** | The panel builds `TransactionDraft` objects and passes them unchanged to `RegisterService.SaveAsync`, which builds `Money` values through exact arithmetic. No floating point, no rounding side-effects in the panel itself. |
| **2 — Amounts signed from owning account's POV; transfer as two rows** | `EntryModeMapper` encodes the business rule: payment/deposit direction determines the sign placed on the `Money` amount; transfer mode sets `TransferAccountId` and clears category. Spec 002 FR-018/026–029 are preserved byte-for-byte. |
| **3 — Every write through a service in MyFinance.Data/Services** | The panel calls `RegisterService.SaveAsync(draft)` exclusively. No `DbContext` instantiation, no raw SQL, no other write path. The modal did this already; the panel does not change it. |
| **4 — Correctness lives outside WPF** | `EntryModeMapper` lives in `MyFinance.Core/Registers` and is unit-tested in `MyFinance.Core.Tests/Registers`. Validation (`TransactionValidator`) and amount parsing (`Money.TryParse`) already live there. Only the panel's `IsOpen`/`IsDirty` state and field binding is WPF-specific. |
| **9 — Long work off UI thread, cancellable, transactional** | `RegisterService.SaveAsync` already does this internally. The panel calls it with a `CancellationToken` when save begins; cancellation restores the panel state. The save is transactional — commit or rollback, never half-written. |

## Technical context

- **Projects touched:** `MyFinance.Core` (new `Registers/EntryModeMapper.cs`), `MyFinance.App`
  (new panel files, modified register page), `MyFinance.Data/Services` (one new
  `SettingsService` key for panel visibility), no schema changes.
- **Dependencies:** none new. `CommunityToolkit.Mvvm` 8.4.0 (already required by App).
  `FlowDocument`, `BooleanToVisibilityConverter` — WPF framework only.
- **Existing pieces reused, not rebuilt:**
  - `RegisterService.SaveAsync` / `RegisterService.DeleteAsync` / `RegisterService.SetClearedStatusAsync`
    (all unmodified).
  - `TransactionValidator.Validate` / `BookValidationException` (unchanged).
  - `SuggestionService.SuggestAsync`, payee reuse and last-used memory (unchanged).
  - `PayeeService.FindByNameAsync`, `SplitEditorViewModel` (unchanged; opened identically via
    `IModalService.Show`).
  - `RegisterPageViewModel.RefreshAsync` (called once panel completes a save, to recompute the
    register).
- **Testing:** `MyFinance.Core.Tests/Registers/EntryModeMapperTests` (new). Everything else
  reuses existing coverage: `RegisterServiceTests`, `TransactionValidatorTests`,
  `SuggestionServiceTests`, `PayeeServiceTests`. The panel's own UI behavior (toggle, field
  binding, dirty-state tracking) **needs Windows** — the existing `TransactionEditorViewModel`
  unit test coverage is exactly zero, and spec 020 inherits that honest gap.

## Design

### EntryModeMapper: the one new decidable piece

A small utility in `MyFinance.Core/Registers` that encodes spec 002 FR-018/026:

```
EntryMode (Payment | Deposit | Transfer)
  + TransferAccountId?
  + CategoryId?
  => (signedAmount: Money, categoryId: int?, transferAccountId: int?)
```

Example: `Payment` mode + $50 entered + no target account → `(-50 cents, categoryId, null)`.
`Transfer` mode + $50 + target account 7 → `(-50 cents, null, 7)`.

This is pure logic, no IO, and the panel builds one every keystroke to re-validate field
visibility (show category in Payment/Deposit only, show target account in Transfer only). By
moving it here and testing it, we keep the panel's own `[RelayCommand]` handlers from being
an untested black box, and we honour Principle 4 by preventing the one new piece of logic
from becoming WPF-only.

### RegisterEntryPanelViewModel

New `[ObservableObject]` partial class in `ViewModels/Controls/`, built and held by
`RegisterPageViewModel` (not DI-registered, same as today's `TransactionEditorViewModel`).
Owns the panel's lifecycle:

- `[ObservableProperty]` fields: `IsOpen`, `IsDirty`, `Mode`, `Date`, `Number`, `PayeeName`,
  `Memo`, `AmountText`, `Category`, `TransferAccount`, `IsCleared`, `IsVoid`,
  `SuggestionText`, `HasSuggestion`, `ErrorMessage`, `CategorySummary`, `IsSplit`.
- `[RelayCommand]` methods: `NewAsync`, `EditAsync(RegisterRowViewModel)`, `SaveAsync`,
  `CancelAsync`, `EditSplits`, `UseSuggestion`, `SuggestCategoryAsync` (payee lost focus).
- Constructor: receives `RegisterService`, `PayeeService`, `SuggestionService`, `IModalService`
  as parameters (reuses `RegisterPageViewModel`'s injected instances).

The panel:
1. **For New**: clears all fields, sets `Mode = Payment`, sets `IsOpen = true`, moves focus to
   payee.
2. **For Edit**: populates from the selected row, detects its mode (`IsTransfer` → Transfer,
   else Payment if amount < 0 else Deposit), sets `IsOpen = true`.
3. **On Save**: builds `TransactionDraft`, calls `RegisterService.SaveAsync`, clears fields
   for the next entry, **leaves `IsOpen = true`**. Updates `RegisterPageViewModel.Rows` and
   recomputes running balance via `RefreshAsync`.
4. **On Cancel**: clears `ErrorMessage`, sets `IsDirty = false`, **leaves `IsOpen = true`**.
5. **On row selection**: if `IsDirty`, prompts (save/discard/cancel); "cancel" reverts the
   selection; otherwise loads the new row (same as Edit above).
6. **Closes only on** explicit user action (Hide button) or app shutdown — never auto-closes.

### RegisterEntryPanel UserControl

New `Views/Controls/RegisterEntryPanel.xaml` + code-behind. Structure mirrors the existing
modal's tabs and fields, but housed in a `UserControl` instead of a `Window`. Binds `IsOpen`
to visibility via `BooleanToVisibilityConverter` (same pattern as `BusyOverlay.xaml`).

Three mode-specific `Grid`s for Payment, Deposit, Transfer modes — only the active one is
shown via `Visibility` binding on `VM.Mode == Payment` etc. Fields (payee, category, etc.)
appear / hide per mode (`EntryModeMapper` drives this).

Keyboard: Tab through fields in a sensible order; Enter saves; Escape cancels (unchanged).

### RegisterPage.xaml layout

Current 3-row grid:

```xaml
<Grid.RowDefinitions>
  <RowDefinition Height="Auto" />  <!-- toolbar -->
  <RowDefinition Height="*" />     <!-- DataGrid register -->
  <RowDefinition Height="Auto" />  <!-- footer -->
</Grid.RowDefinitions>
```

Becomes 4-row:

```xaml
<Grid.RowDefinitions>
  <RowDefinition Height="Auto" />  <!-- toolbar (Ctrl+N, filter, etc.) -->
  <RowDefinition Height="*" />     <!-- DataGrid register -->
  <RowDefinition Height="Auto" />  <!-- new: RegisterEntryPanel, visibility bound -->
  <RowDefinition Height="Auto" />  <!-- footer -->
</Grid.RowDefinitions>
```

Add a new row hosting `<local:RegisterEntryPanel DataContext="{Binding EntryPanel}" />`.

### RegisterPageViewModel changes

Minimal, following the existing pattern:
- Add an `EntryPanel` property (`RegisterEntryPanelViewModel`), constructed in the constructor
  with the same injected services (`RegisterService`, `PayeeService`, etc.).
- Modify `NewTransactionAsync` → calls `EntryPanel.NewAsync()` instead of `_modals.Show(...)`.
- Modify `EditTransactionAsync(row)` → calls `EntryPanel.EditAsync(row)` instead of
  `_modals.Show(...)`.
- Remove `DeleteTransactionAsync` (the modal's "Delete" button) — register Delete/Ctrl+Delete
  stays; it calls `RegisterService.DeleteAsync` directly, unaffected.
- Hook `EntryPanel.PropertyChanged` to detect `IsDirty` changes and warn on row selection
  (clarification 2's prompt logic).

### SettingsService changes

Add one key: `"panel.entry.isopen"` (boolean, default false). Read on startup, written on
toggle. Replaces the need for a `RegisterPageViewModel` singleton to preserve open/close
state — the preference is app-wide, not per-page-instance.

### App.xaml.cs changes

Remove the modal registration: `modals.Register<TransactionEditorViewModel>(...)`.
`SplitEditorViewModel` registration stays (split editor remains a modal).

### Retirement of the modal

Once the panel reaches feature parity with the modal on Acceptance scenarios 1–9 from spec
020:
1. Delete `src/MyFinance.App/ViewModels/Dialogs/TransactionEditorViewModel.cs`.
2. Delete `src/MyFinance.App/Views/Dialogs/TransactionEditorWindow.xaml(.cs)`.
3. Verify no other file references these; `grep -r TransactionEditorViewModel` and
   `TransactionEditorWindow` (both should yield only test hits or doc references).
4. Run the full test suite once: `dotnet test MyFinance.slnx`. All spec-002 acceptance
   scenarios must still pass.

### Alternatives rejected

1. **Keep `TransactionEditorWindow` as a fallback.** Tempting for safe gradual migration, but
   two write paths mean two places to enforce the same invariant (spec 002 principle 3). A
   modal and a panel would drift over time; the one that gets tested (hint: the modal, because
   it's used less) would become the bug-prone path. Cleaner to migrate wholesale and remove
   immediately.
2. **Make the split editor inline too.** Out of scope, and `SplitEditorViewModel` is already
   testable (used by nothing else); opening it from an inline panel works perfectly via the
   existing `IModalService`. Redesigning it would be future scope, not this feature's.
3. **Auto-save on row switch.** Violates principle 3 (an unseen write). **Silently discard on
   row switch.** Violates principle 3 and loses user data. **Prompt.** Matches the existing
   precedent (`DeleteTransactionCommand` asks for confirmation); chosen.

## Project structure

```
src/MyFinance.Core/Registers/EntryModeMapper.cs                       NEW
tests/MyFinance.Core.Tests/Registers/EntryModeMapperTests.cs          NEW

src/MyFinance.App/ViewModels/Controls/RegisterEntryPanelViewModel.cs   NEW
src/MyFinance.App/Views/Controls/RegisterEntryPanel.xaml              NEW
src/MyFinance.App/Views/Controls/RegisterEntryPanel.xaml.cs           NEW

src/MyFinance.App/ViewModels/Pages/RegisterPageViewModel.cs           MODIFIED
src/MyFinance.App/Views/Pages/RegisterPage.xaml                       MODIFIED

src/MyFinance.Data/Services/SettingsService.cs                        MODIFIED (one key)
src/MyFinance.App/App.xaml.cs                                         MODIFIED (drop modal)

src/MyFinance.App/ViewModels/Dialogs/TransactionEditorViewModel.cs    REMOVED
src/MyFinance.App/Views/Dialogs/TransactionEditorWindow.xaml(.cs)     REMOVED
```

## Risks

1. **WPF-only and unverifiable in CI.** Same honest framing as `016-printing/plan.md`: the
   panel's `IsOpen`/`IsDirty` state, field binding, and keyboard navigation **needs a person
   on Windows to verify**. The decision logic (`EntryModeMapper`) is fully testable; the UI
   binding is not. The coverage table in `tasks.md` will record this distinction, not hide it
   behind a ✅ that implies more than is true.
2. **Deleting the modal is a regression risk.** Mitigated by checking every spec-002
   acceptance scenario once the panel is feature-complete: acceptance 3 (split must sum),
   acceptance 4 (transfer pair must link), acceptance 7 (cleared reconciliation), etc. If the
   panel passes all of them, the logic is safe; if it doesn't, the deletion is deferred until
   it does.
3. **Focus containment weaker than a modal.** A modal naturally contains keyboard focus (Tab
   loops within it, Escape closes it). A docked panel does not — Tab can escape to the
   register grid. Mitigation: explicit focus management in the XAML
   (`FocusManager.FocusedElement` on panel open), and the tasks.md list marks keyboard
   verification **⚠️ needs a person**.
4. **A long future of modal-free design precedent.** Once this ships, every future feature
   that needs a dialog-like interaction will look at it and say "just do what the panel does."
   Not every dialog-able feature should be a panel (e.g., destructive confirmations, rare
   multi-step wizards); the precedent is set by what this feature *is* (repeated, in-flow data
   entry), not by its mechanism. Worth stating up front rather than discovering later.

## What changed during planning

Nothing yet — this is a forward-looking plan, not a post-mortem. The tasks will record
changes if implementation diverges from the design above.
