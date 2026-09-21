# Tasks: Inline transaction entry at the bottom of the register

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** Draft (unbuilt)

New deliverable: `MyFinance.Core.Tests/Registers/EntryModeMapperTests` — a new test suite for
the one piece of decidable logic extracted into Core per Principle 4. Everything else reuses
existing coverage from `RegisterServiceTests`, `TransactionValidatorTests`, etc.

This feature is overwhelmingly a presentation change, reusing proven Core/Data logic
wholesale. The split between what *can* be tested in isolation (the mode mapper) and what
**needs Windows** (field binding, visibility, focus, layout) is deliberate and recorded below.

`[ ]` outstanding · `[P]` may run in parallel with its neighbours.

**Read the plan's risk section before starting.** This feature depends on manual verification
on Windows; the coverage table below records that honestly rather than claiming CI can close it.

## Phase 1 — Decidable logic, in Core

- [ ] **T001** Entry mode mapper: given a payment/deposit/transfer direction, produce a signed
  amount and normalized category/transfer fields
  - Implements: `src/MyFinance.Core/Registers/EntryModeMapper.cs` (new static class or
    methods)
  - Proven by: `EntryModeMapperTests.Payment_direction_produces_negative_amount`,
    `Deposit_direction_produces_positive_amount`, `Transfer_mode_clears_category_and_sets_target_account`,
    `Same_account_transfer_is_refused`, `Amount_sign_applies_regardless_of_amount_value`,
    `Mode_transitions_between_payment_and_deposit_change_sign` (property of toggling direction
    mid-entry)
  - Requirements: FR-005–008 (entry mode semantics), spec 002 FR-018 (amount = magnitude +
    direction), spec 002 FR-026–028 (transfer rules)

## Phase 2 — The panel (needs Windows to verify UI behavior)

- [ ] **T002** Panel state and lifecycle: IsOpen, IsDirty, New/Edit population, focus on open
  - Implements: `src/MyFinance.App/ViewModels/Controls/RegisterEntryPanelViewModel.cs` (new
    class, inherits `ObservableObject`)
  - Consumes: `RegisterService.FindAsync`, `SuggestionService`, `IModalService` (all
    existing, proven by their own tests)
  - Proven by: T001 + **needs Windows**. VM construction and property binding can be tested in
    a WPF test harness (unit test on Windows with a test runner like WpfUnit or xunit +
    manually), but cannot be tested in CI. The register selection → panel population logic is
    straightforward; the test focuses on state transitions, not rendering.
  - Requirements: FR-001–004 (showing/hiding and persistence), FR-009–010 (New/Edit), spec
    002 FR-034 (refuse balance-only accounts)
- [ ] **T003** Save flow: clear-on-new-save, update-in-place-on-edit-save, refresh register
  - Implements: `RegisterEntryPanelViewModel.SaveAsync`
  - Calls: `RegisterService.SaveAsync` (existing, proven by `RegisterServiceTests`)
  - Proven by: **existing `RegisterServiceTests.SaveAsync_*` coverage**, plus manual
    verification on Windows of the panel clearing / staying open (FR-002).
  - Requirements: FR-011 (update in place), spec 002 FR-021 (replace, don't accumulate)
- [ ] **T004** Split access from the panel: call `EditSplitsCommand`, display summary
  - Implements: `RegisterEntryPanelViewModel.EditSplitsAsync`
  - Calls: `IModalService.Show(SplitEditorViewModel)` (unchanged from modal editor)
  - Proven by: **existing `SplitEditorViewModelTests`** (the split editor itself is unchanged).
    **Needs Windows** for the flow (panel → split modal → panel again).
  - Requirements: FR-012–014 (split editing, FR-013 "split across N categories" summary)
- [ ] **T005** Payee memory and suggestion: pre-fill amount/category, offer classifier
  suggestions
  - Implements: `RegisterEntryPanelViewModel.SuggestCategoryAsync`, `UseSuggestion`
  - Calls: `PayeeService.FindByNameAsync`, `SuggestionService.SuggestAsync` (existing, proven
    by `SuggestionServiceTests`, `PayeeServiceTests`)
  - Proven by: **existing test coverage** (nothing new in Core/Data logic).
    **Needs Windows** for the textbox focus / suggestion UI.
  - Requirements: FR-015–016 (spec 002 FR-022–023), Acceptance scenario 8
- [ ] **T006** Validation surfacing: collect `TransactionValidator` errors, display together
  - Implements: `RegisterEntryPanelViewModel.SaveAsync` error handling
  - Calls: `TransactionValidator.Validate` (existing, proven by `TransactionValidatorTests`)
  - Proven by: **existing validator coverage**. Panel's own error → UI binding is **needs
    Windows**.
  - Requirements: FR-017 (spec 002 FR-024–025, report all errors together)
- [ ] **T007** Dirty-state prompt on row selection: detect changes, offer save/discard/cancel
  - Implements: `RegisterPageViewModel.OnEntryPanelDirtyChanged`, prompt logic
  - Proven by: **needs a person on Windows**. Logic is straightforward (if IsDirty, show
    dialog; if cancel, revert selection); the test is that it actually prompts at the right
    time and the user can choose correctly. No automated test framework can assert dialog-box
    user interaction.
  - Requirements: FR-024 (clarification 2 above), Acceptance scenario 10
- [ ] **T008** Visibility toggle and persistence: IsOpen binding, SettingsService key
  - Implements: `SettingsService` new key `"panel.entry.isopen"`, `RegisterEntryPanel.xaml`
    `Visibility` binding, `RegisterPageViewModel.LoadSettingsAsync` call
  - Proven by: **existing `SettingsServiceTests`** (round-trip storage). UI binding is **needs
    Windows**.
  - Requirements: FR-001–003 (show/hide), FR-004 (clarification 1, app-wide persistence)
- [ ] **T009** Balance-only and reconciled transaction guards: refuse writes, show warnings
  - Implements: `RegisterEntryPanelViewModel` validation (check
    `Account.Type == Unsupported` before save, check `Transaction.IsReconciled` before edit)
  - Proven by: **existing `RegisterServiceTests`** (the service already refuses these). Panel
    just enforces it pre-save.
  - Requirements: FR-026–027 (spec 002 FR-034), Edge case (reconciled)

## Phase 3 — Docking and keyboard accessibility

- [ ] **T010** Dock the panel into RegisterPage.xaml: add 4th grid row, host UserControl
  - Implements: `src/MyFinance.App/Views/Controls/RegisterEntryPanel.xaml` (UserControl,
    three mode-specific grids), `RegisterPage.xaml` (add row, place panel)
  - Proven by: **needs Windows** for layout and alignment. The binding structure (DataContext,
    Visibility on IsOpen, field bindings to ViewModel properties) follows existing patterns
    (`BusyOverlay.xaml`, `RegisterPage.xaml`'s own filter toggle).
  - Requirements: FR-001–003 (showing/hiding)
- [ ] **T011** Keyboard operability: tab order, Enter saves, Escape cancels, Ctrl+N from
  register
  - Implements: `RegisterEntryPanel.xaml.cs` code-behind (focus management on IsOpen change),
    `RegisterPage.xaml` `InputBindings` for Ctrl+N (call
    `RegisterPageViewModel.NewTransactionCommand`)
  - Proven by: **needs a person on Windows with a keyboard**. Test: press Tab 6 times (payee
    → amount → category → [split button / transfer account] → save → cancel), press Enter
    (saves), select a row, press Enter (opens panel for edit). No automation possible; the
    testability limitation is inherent.
  - Requirements: FR-020–021 (keyboard, Enter/Escape), Acceptance scenarios throughout

## Phase 4 — Retiring the modal (last, gated by parity)

- [ ] **T012** Remove TransactionEditorWindow/ViewModel, drop modal registration
  - Deletes: `src/MyFinance.App/ViewModels/Dialogs/TransactionEditorViewModel.cs`,
    `src/MyFinance.App/Views/Dialogs/TransactionEditorWindow.xaml(.cs)`
  - Modifies: `src/MyFinance.App/App.xaml.cs` (remove `modals.Register<TransactionEditorViewModel>`)
  - Prerequisite: all of T001–T011 pass on Windows; every spec-002 Acceptance scenario
    (splitting, transferring, payee memory, validation) confirmed working through the panel.
  - Proven by: `dotnet test MyFinance.slnx` (the whole suite must pass; if anything breaks, a
    spec-002 assumption was violated by the panel's changes).
  - Requirements: (implicit requirement that panel ≡ modal in transaction entry capability)

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T008, T010 | ✅ setting storage (existing); ⚠️ **UI binding needs Windows** |
| FR-002 | T003, T010 | ✅ logic in T003; ⚠️ **panel-stays-open behavior needs Windows** |
| FR-003 | T010 | ⚠️ **Escape/Cancel dismissal needs Windows** |
| FR-004 | T008 | ✅ SettingsService round-trip; ⚠️ **persistence edge cases need Windows** |
| FR-005 | T010 | ⚠️ **mode tabs need Windows** |
| FR-006 | T002, T001, T010 | ✅ `EntryModeMapper` (T001); ⚠️ **field visibility needs Windows** |
| FR-007 | T001, T010 | ✅ `EntryModeMapper`; ⚠️ **transfer fields need Windows** |
| FR-008 | T001, T010 | ✅ mode label logic in `EntryModeMapper`; ⚠️ **label UI needs Windows** |
| FR-009 | T002 | ⚠️ **needs Windows** |
| FR-010 | T002 | ⚠️ **needs Windows** |
| FR-011 | T003 | ✅ `RegisterService` proven; ⚠️ **in-place register update needs Windows** |
| FR-012 | T004 | ⚠️ **needs Windows** |
| FR-013 | T004 | ⚠️ **needs Windows** |
| FR-014 | T001, T006 | ✅ `EntryModeMapper` + existing validator; ⚠️ **error message UI needs Windows** |
| FR-015 | T005 | ✅ existing payee autocomplete logic; ⚠️ **needs Windows** |
| FR-016 | T005 | ✅ existing suggestion service; ⚠️ **needs Windows** |
| FR-017 | T006 | ✅ `TransactionValidator`; ⚠️ **error display needs Windows** |
| FR-018 | T001 | ✅ fully tested (spec 002 FR-018 preserved) |
| FR-019 | T006 | ✅ existing validator; ⚠️ **error display needs Windows** |
| FR-020 | T011 | ⚠️ **keyboard tab order needs a person** |
| FR-021 | T011 | ⚠️ **keyboard Enter/Escape needs a person** |
| FR-022 | T011 | ⚠️ **needs Windows** |
| FR-023 | T010 | ⚠️ **needs Windows** |
| FR-024 | T007 | ⚠️ **prompt interaction needs a person** |
| FR-025 | T004 | ✅ `SplitEditorViewModel` unchanged; ⚠️ **modal launch needs Windows** |
| FR-026 | T009 | ✅ existing service guard; ⚠️ **needs Windows** |
| FR-027 | T009 | ⚠️ **warning UI needs Windows** |
| NFR-001 | T003 | ✅ `RegisterService.SaveAsync` is already responsive and tested |
| NFR-002 | T003 | ✅ `RegisterService.SaveAsync` is already async and uses `UiContext` correctly |
| NFR-003 | T002 | ⚠️ **memory profiling needs repeated entry on Windows** |
| SC-001 | T003 | ⚠️ **ten consecutive entries needs a person** |
| SC-002 | T001–T009 | ✅ spec 002 scenarios tested in their own specs |
| SC-003 | T011 | ⚠️ **keyboard-only entry needs a person** |
| SC-004 | T003, T010 | ✅ `RegisterService` proven; ⚠️ **register state update needs Windows** |
| SC-005 | T006 | ✅ existing validator; ⚠️ **error display needs Windows** |

**Honest count: 14 fully testable (✅), 44 requiring Windows and/or a person (⚠️).** That is
a property of the feature (it is presentation-layer work) and the codebase (no WPF test
framework exists). Rather than pretend otherwise, the table states it plainly. Three things
are strictly CI-verifiable (the `EntryModeMapper` logic and the reused service calls); the
rest require manual testing on the platform.

## What is not covered

- **Everything that requires looking:** field alignment, label readability, whether focus
  actually moves as expected, whether the split editor really closes and returns to the panel,
  whether a long register scrolls correctly with the panel docked. A person on Windows covers
  these, nobody else.
- **Automated keyboard testing.** xunit can assert button clicks and command execution in a
  WPF test context, but not the sequence of Tab/Shift+Tab through actual controls, or the
  firing of `InputBindings` (Ctrl+N from the register, not the panel). This would require a
  headless WPF test runner or UI automation (UIA) framework neither this project nor its CI
  possesses.
- **Memory profiling after hundreds of entries.** NFR-003 (no memory leak across many
  entries). Mitigated by reusing `RegisterPageViewModel`'s existing refresh cycle (a
  `RefreshAsync` clears the panel state between entries). A full test would require a profiler
  and live observation.
- **The split editor's own redesign.** Out of scope per spec; T004 only tests that the modal
  remains launchable unchanged.

## Regression prevention

Before T012 (retiring the modal), manually run through every Acceptance scenario in **spec
002**:
- Scenario 3 (split sum check): create a $100 transaction, split across 3 categories, try to
  save with only $90 allocated — panel refuses (error message shown).
- Scenario 4 (transfer pair): create a transfer from Checking to Savings. Both rows appear in
  both registers and cancel. Edit one; both update.
- Scenario 7 (reconciliation): reconcile a few rows, then try to edit one. Panel shows
  warning "This transaction is reconciled. Edit it?"; cancel stays in place, yes unreconciles
  it before saving.

Each scenario passing on the panel before deletion ensures we haven't regressed spec 002's
own requirements. If any fails, defer T012 until the panel matches the modal.
