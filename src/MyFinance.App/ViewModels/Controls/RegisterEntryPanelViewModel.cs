using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;
using MyFinance.Data.Services;
using MyFinance.Import.Categorization;

namespace MyFinance.App.ViewModels.Controls;

/// <summary>
/// Inline transaction entry panel docked at the bottom of the register.
/// Stays open across entries, clears on save, remains available for the next entry.
/// </summary>
public partial class RegisterEntryPanelViewModel : ObservableObject
{
    private readonly RegisterService _register;
    private readonly PayeeService _payees;
    private readonly SuggestionService _suggestions;
    private readonly IModalService _modals;
    private readonly IDialogService _dialogs;
    private readonly int? _currentAccountId;

    private int? _editingId;
    private IReadOnlyList<SplitDraft> _splits = [];
    private bool _isReconciled;
    private CategoryListItem? _suggested;

    public RegisterEntryPanelViewModel(
        RegisterService register,
        PayeeService payees,
        SuggestionService suggestions,
        IModalService modals,
        IDialogService dialogs,
        int? currentAccountId,
        IReadOnlyList<Account> transferTargets,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<string> payeeNames)
    {
        _register = register;
        _payees = payees;
        _suggestions = suggestions;
        _modals = modals;
        _dialogs = dialogs;
        _currentAccountId = currentAccountId;

        TransferTargets = [.. transferTargets.Where(a => _currentAccountId is null || a.Id != _currentAccountId)];
        Categories = categories;
        PayeeNames = payeeNames;

        _date = DateTime.Today;
        _mode = EntryMode.Payment;
    }

    public IReadOnlyList<Account> TransferTargets { get; private set; }

    public IReadOnlyList<CategoryListItem> Categories { get; private set; }

    public IReadOnlyList<string> PayeeNames { get; private set; }

    public void UpdateLists(
        IReadOnlyList<Account> transferTargets,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<string> payeeNames)
    {
        TransferTargets = [.. transferTargets.Where(a => _currentAccountId is null || a.Id != _currentAccountId)];
        Categories = categories;
        PayeeNames = payeeNames;

        OnPropertyChanged(nameof(TransferTargets));
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(PayeeNames));
    }

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private EntryMode _mode = EntryMode.Payment;

    [ObservableProperty]
    private DateTime _date = DateTime.Today;

    [ObservableProperty]
    private string? _number;

    [ObservableProperty]
    private string? _payeeName;

    [ObservableProperty]
    private string? _memo;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private CategoryListItem? _category;

    [ObservableProperty]
    private string _suggestionText = string.Empty;

    [ObservableProperty]
    private bool _hasSuggestion;

    [ObservableProperty]
    private Account? _transferAccount;

    [ObservableProperty]
    private bool _isCleared;

    [ObservableProperty]
    private bool _isVoid;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>True when the allocation is spread across more than one category.</summary>
    public bool IsSplit => _splits.Count > 1;

    /// <summary>A transfer is not spending, so it takes no category.</summary>
    public bool IsTransfer => TransferAccount is not null;

    public bool CanPickCategory => !IsSplit && !IsTransfer;

    /// <summary>What the category field shows: one category, "Split", or nothing.</summary>
    public string CategorySummary
    {
        get
        {
            if (IsTransfer)
            {
                return $"Transfer: {TransferAccount!.Name}";
            }

            return _splits.Count switch
            {
                > 1 => $"Split across {_splits.Count} categories",
                _ => Category?.FullName ?? "Uncategorized",
            };
        }
    }

    /// <summary>Warns that saving will unsettle an item a completed reconciliation locked in.</summary>
    public bool IsReconciledWarningVisible => _isReconciled;

    /// <summary>Start a new transaction entry.</summary>
    [RelayCommand]
    public void New()
    {
        ClearAll();
        IsOpen = true;
        IsDirty = false;
    }

    /// <summary>Edit an existing transaction in the panel.</summary>
    [RelayCommand]
    public void Edit(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        _editingId = transaction.Id;
        Date = transaction.Date.ToDateTime(TimeOnly.MinValue);
        Number = transaction.Number;
        PayeeName = transaction.Payee?.Name;
        Memo = transaction.Memo;

        // Infer mode from the transaction type
        if (transaction.TransferPeer is not null)
        {
            Mode = EntryMode.Transfer;
            TransferAccount = TransferTargets.FirstOrDefault(a => a.Id == transaction.TransferPeer.AccountId);
        }
        else
        {
            Mode = transaction.Amount.IsNegative ? EntryMode.Payment : EntryMode.Deposit;
        }

        AmountText = transaction.Amount.Abs().ToString("N", CultureInfo.CurrentCulture);
        IsCleared = transaction.ClearedStatus != ClearedStatus.Uncleared;
        IsVoid = transaction.IsVoid;
        _isReconciled = transaction.ClearedStatus == ClearedStatus.Reconciled;

        _splits =
        [
            .. transaction.Splits.Select(s => new SplitDraft
            {
                CategoryId = s.CategoryId,
                Amount = s.Amount,
                Memo = s.Memo,
            })
        ];

        if (_splits.Count == 1)
        {
            Category = Categories.FirstOrDefault(c => c.Id == _splits[0].CategoryId);
        }

        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(IsTransfer));
        OnPropertyChanged(nameof(CanPickCategory));
        OnPropertyChanged(nameof(CategorySummary));

        IsOpen = true;
        IsDirty = false;
        ErrorMessage = null;
    }

    /// <summary>Drops back to a single category, discarding the split lines.</summary>
    [RelayCommand]
    private void ClearSplits()
    {
        _splits = [];
        RefreshCategoryState();
        MarkDirty();
    }

    /// <summary>Open the split editor.</summary>
    [RelayCommand]
    private void EditSplits()
    {
        if (!TryReadAmount(out Money amount))
        {
            ErrorMessage = "Enter the amount first, so the split lines have a total to add up to.";
            return;
        }

        IReadOnlyList<SplitDraft> starting = _splits.Count > 1
            ? _splits
            : Category is null
                ? []
                : [new SplitDraft { CategoryId = Category.Id, Amount = amount }];

        var editor = new SplitEditorViewModel(amount, Categories, starting);

        if (_modals.Show(editor))
        {
            _splits = editor.Result;

            Category = _splits.Count == 1
                ? Categories.FirstOrDefault(c => c.Id == _splits[0].CategoryId)
                : null;

            RefreshCategoryState();
            MarkDirty();
        }
    }

    /// <summary>Recommend a category once the payee is known.</summary>
    [RelayCommand]
    private async Task SuggestCategoryAsync()
    {
        ClearSuggestion();

        if (IsSplit || IsTransfer || string.IsNullOrWhiteSpace(PayeeName))
        {
            return;
        }

        string payeeName = PayeeName;

        // Pre-fill amount on new entry only
        if (_editingId is null && string.IsNullOrWhiteSpace(AmountText))
        {
            PayeeListItem? payee = await Task
                .Run(() => _payees.FindByNameAsync(payeeName))
                .ConfigureAwait(true);

            if (payee?.LastAmount is Money last)
            {
                AmountText = last.Abs().ToString("N", CultureInfo.CurrentCulture);
                Mode = last.IsNegative ? EntryMode.Payment : EntryMode.Deposit;
                MarkDirty();
            }
        }

        if (Category is not null)
        {
            return;
        }

        if (_currentAccountId is null)
        {
            return;
        }

        var request = new SuggestionRequest(_currentAccountId.Value, payeeName, SignedAmount(), Memo);

        CategorySuggestion suggestion = await Task
            .Run(() => _suggestions.SuggestAsync(request))
            .ConfigureAwait(true);

        // User may have moved on; discard stale suggestions
        if (Category is not null || !string.Equals(PayeeName, payeeName, StringComparison.Ordinal))
        {
            return;
        }

        if (suggestion.CategoryId is not int suggested)
        {
            return;
        }

        CategoryListItem? category = Categories.FirstOrDefault(c => c.Id == suggested);

        if (category is null)
        {
            return;
        }

        if (suggestion.IsCertain)
        {
            Category = category;
            SuggestionText = suggestion.Describe();
            return;
        }

        // A guess: shown, not applied
        _suggested = category;
        SuggestionText = $"Suggested: {category.FullName} — {suggestion.Describe()}";
        HasSuggestion = true;
    }

    /// <summary>Accept the offered category suggestion.</summary>
    [RelayCommand]
    private void UseSuggestion()
    {
        if (_suggested is null)
        {
            return;
        }

        CategoryListItem accepted = _suggested;
        Category = accepted;
        ClearSuggestion();
        MarkDirty();
    }

    /// <summary>Save the transaction and clear for the next entry. Panel stays open.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (_isReconciled)
        {
            if (!_dialogs.Confirm(
                "Unreconcile transaction",
                "This transaction is reconciled. Edit it?"))
            {
                return;
            }
        }

        if (_currentAccountId is null)
        {
            ErrorMessage = "No account selected.";
            return;
        }

        if (!TryReadAmount(out Money amount))
        {
            ErrorMessage = "The amount is not a number.";
            return;
        }

        IReadOnlyList<SplitDraft> splits;

        if (IsTransfer)
        {
            splits = [];
        }
        else if (_splits.Count > 1)
        {
            Money assigned = Money.Sum(_splits.Select(s => s.Amount));
            if (assigned != amount)
            {
                ErrorMessage =
                    $"The split lines add up to {assigned.ToAccountingString(CultureInfo.CurrentCulture)} but the transaction is now {amount.ToAccountingString(CultureInfo.CurrentCulture)}. Reopen the split to fix it.";
                return;
            }

            splits = _splits;
        }
        else
        {
            splits = [new SplitDraft { CategoryId = Category?.Id, Amount = amount }];
        }

        var draft = new TransactionDraft
        {
            Id = _editingId,
            AccountId = _currentAccountId.Value,
            Date = DateOnly.FromDateTime(Date),
            Number = Number,
            PayeeName = PayeeName,
            Memo = Memo,
            Amount = amount,
            ClearedStatus = ResolveClearedStatus(),
            IsVoid = IsVoid,
            TransferAccountId = TransferAccount?.Id,
            Splits = splits,
        };

        try
        {
            await _register.SaveAsync(draft).ConfigureAwait(true);
            ClearAll();
            IsDirty = false;
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>Discard unsaved changes. Panel stays open.</summary>
    [RelayCommand]
    public void Cancel()
    {
        ErrorMessage = null;
        ClearSuggestion();
        IsDirty = false;
        // Panel stays open for the next entry
    }

    /// <summary>The amount as the rules see it, with sign.</summary>
    private Money SignedAmount()
    {
        if (!Money.TryParse(AmountText, CultureInfo.CurrentCulture, out Money amount))
        {
            return Money.Zero;
        }

        return EntryModeMapper.MapAmount(Mode, amount);
    }

    private bool TryReadAmount(out Money amount)
    {
        if (!Money.TryParse(AmountText, CultureInfo.CurrentCulture, out Money entered))
        {
            amount = Money.Zero;
            return false;
        }

        amount = EntryModeMapper.MapAmount(Mode, entered);
        return true;
    }

    private ClearedStatus ResolveClearedStatus()
    {
        if (!IsCleared)
        {
            return ClearedStatus.Uncleared;
        }

        return _isReconciled ? ClearedStatus.Reconciled : ClearedStatus.Cleared;
    }

    private void ClearAll()
    {
        _editingId = null;
        Date = DateTime.Today;
        Number = null;
        PayeeName = null;
        Memo = null;
        Mode = EntryMode.Payment;
        AmountText = string.Empty;
        Category = null;
        TransferAccount = null;
        IsCleared = false;
        IsVoid = false;
        _isReconciled = false;
        _splits = [];
        ClearSuggestion();
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(IsTransfer));
        OnPropertyChanged(nameof(CanPickCategory));
        OnPropertyChanged(nameof(CategorySummary));
        OnPropertyChanged(nameof(IsReconciledWarningVisible));
    }

    private void RefreshCategoryState()
    {
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(IsTransfer));
        OnPropertyChanged(nameof(CanPickCategory));
        OnPropertyChanged(nameof(CategorySummary));
    }

    private void ClearSuggestion()
    {
        _suggested = null;
        SuggestionText = string.Empty;
        HasSuggestion = false;
    }

    private void MarkDirty()
    {
        IsDirty = true;
    }

    partial void OnCategoryChanged(CategoryListItem? value)
    {
        ClearSuggestion();
        RefreshCategoryState();
        MarkDirty();
    }

    partial void OnTransferAccountChanged(Account? value)
    {
        if (value is not null)
        {
            _splits = [];
            Category = null;
        }

        RefreshCategoryState();
        MarkDirty();
    }

    partial void OnModeChanged(EntryMode value)
    {
        RefreshCategoryState();
        MarkDirty();
    }

    partial void OnDateChanged(DateTime value)
    {
        MarkDirty();
    }

    partial void OnNumberChanged(string? value)
    {
        MarkDirty();
    }

    partial void OnPayeeNameChanged(string? value)
    {
        MarkDirty();
    }

    partial void OnMemoChanged(string? value)
    {
        MarkDirty();
    }

    partial void OnAmountTextChanged(string value)
    {
        MarkDirty();
    }

    partial void OnIsClearedChanged(bool value)
    {
        MarkDirty();
    }

    partial void OnIsVoidChanged(bool value)
    {
        MarkDirty();
    }

}
