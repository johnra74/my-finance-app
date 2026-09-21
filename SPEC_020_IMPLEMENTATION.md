# Spec 020 Implementation Status & Guide

## ✅ COMPLETED: Phase 1 (Testable Core Logic)

**T001: EntryModeMapper** — all 24 tests passing

Files created:
- `src/MyFinance.Core/Registers/EntryMode.cs` — enum (Payment, Deposit, Transfer)
- `src/MyFinance.Core/Registers/EntryModeMapper.cs` — utility (MapAmount, normalize category/transfer, validate)
- `tests/MyFinance.Core.Tests/Registers/EntryModeMapperTests.cs` — 24 unit tests

## 🔄 IN PROGRESS: Phase 2-4 (WPF Panel & Modal Retirement)

### Phase 2: Create the Panel (T002-T009)

**T002-T009: RegisterEntryPanelViewModel**
- **File:** `src/MyFinance.App/ViewModels/Controls/RegisterEntryPanelViewModel.cs`
- **Base class:** `ObservableObject` (MVVM Toolkit)
- **Key properties:** `IsOpen`, `IsDirty`, `Mode` (EntryMode), Date, Number, PayeeName, Memo, AmountText, Category, TransferAccount, IsCleared, IsVoid, ErrorMessage, CategorySummary, SuggestionText, HasSuggestion, IsSplit
- **Key commands:** `NewAsync`, `EditAsync`, `SaveAsync`, `CancelAsync`, `EditSplitsAsync`, `UseSuggestionAsync`, `SuggestCategoryAsync`
- **Constructor params:** RegisterService, PayeeService, SuggestionService, IModalService, plus lists (categories, payeeNames, accounts)
- **Reuses unchanged:** PayeeService memory logic, SuggestionService classifier, SplitEditorViewModel (via IModalService)

**T008: Add SettingsService key**
- **File:** `src/MyFinance.Data/Services/SettingsService.cs`
- **New methods:** `GetEntryPanelIsOpenAsync()`, `SetEntryPanelIsOpenAsync(bool)`
- **Key:** `"panel.entry.isopen"`

**T010-T011: Create RegisterEntryPanel UserControl**
- **Files:** 
  - `src/MyFinance.App/Views/Controls/RegisterEntryPanel.xaml` (XAML)
  - `src/MyFinance.App/Views/Controls/RegisterEntryPanel.xaml.cs` (code-behind)
- **Structure:** Three grids (Payment/Deposit/Transfer modes, visibility bound to `Mode`); common fields above; mode-specific fields; Save/Cancel buttons; error display
- **Bindings:** All fields → ViewModel properties; Visibility toggles per mode

### Phase 3: Dock and Integrate (T003, T007, T010-T011 continued)

**T003, T007: Modify RegisterPageViewModel**
- **File:** `src/MyFinance.App/ViewModels/Pages/RegisterPageViewModel.cs`
- **Changes:**
  - Add `RegisterEntryPanelViewModel EntryPanel { get; }` property (construct in ctor)
  - Modify `NewTransactionAsync()`: call `EntryPanel.NewAsync()` instead of modal
  - Modify `EditTransactionAsync(row)`: call `EntryPanel.EditAsync(row)` instead of modal
  - Add dirty-state prompt handler (T007)
  - Hook `EntryPanel.PropertyChanged` to save/restore panel visibility via SettingsService

**T010-T011: Modify RegisterPage.xaml**
- **File:** `src/MyFinance.App/Views/Pages/RegisterPage.xaml`
- **Changes:**
  - Add 4th row to Grid.RowDefinitions (Height="Auto") after DataGrid
  - Host `<local:RegisterEntryPanel DataContext="{Binding EntryPanel}" />`
  - Bind Visibility to `EntryPanel.IsOpen` via BooleanToVisibilityConverter
  - Keyboard: Ctrl+N/Enter/Escape already work via RegisterPageViewModel commands

### Phase 4: Retire Modal (T012, gated by parity)

**T012: Delete TransactionEditorViewModel/Window**
- **Files to delete:**
  - `src/MyFinance.App/ViewModels/Dialogs/TransactionEditorViewModel.cs`
  - `src/MyFinance.App/Views/Dialogs/TransactionEditorWindow.xaml`
  - `src/MyFinance.App/Views/Dialogs/TransactionEditorWindow.xaml.cs`
- **File to modify:**
  - `src/MyFinance.App/App.xaml.cs`: Remove `modals.Register<TransactionEditorViewModel>(...)`
  - Keep: `modals.Register<SplitEditorViewModel>(...)` (split editor stays modal)
- **Gate:** Run `dotnet test MyFinance.slnx` and verify all spec 002 acceptance scenarios pass before deletion

## Testing

### CI (Automated)
- EntryModeMapperTests: 24 tests, all green ✅
- Existing RegisterServiceTests, TransactionValidatorTests, etc.: reused unchanged

### Manual (Windows required) — marks coverage table with ⚠️
- Panel visibility toggle, mode switching, field binding
- Save/edit flow, split access, payee suggestion
- Validation error display, dirty-state prompt
- Keyboard navigation (Tab/Enter/Escape)
- Spec 002 acceptance scenarios (splitting, transferring, payee memory)

## Next Steps

To continue implementation:

1. Create `RegisterEntryPanelViewModel.cs` (200–300 lines, reuse TransactionEditorViewModel logic where possible)
2. Create `RegisterEntryPanel.xaml` + `.xaml.cs` (forms, mode tabs, bindings)
3. Modify `RegisterPageViewModel.cs` and `RegisterPage.xaml` to host the panel
4. Add SettingsService key for persistence
5. Update App.xaml.cs to drop the modal registration
6. Delete the old modal files
7. Run test suite; verify regression tests pass
8. Manual testing on Windows

## Commit Strategy

Each phase as a separate commit:
- Phase 1 ✅ committed
- Phase 2: "spec(020): implement T002-T009 — register entry panel and settings"
- Phase 3: "spec(020): implement T010-T011 — dock panel to register page"
- Phase 4: "spec(020): implement T012 — retire transaction editor modal"
