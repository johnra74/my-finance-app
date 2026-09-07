using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Import.Categorization;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>Which way the money moves, as the editor asks for it.</summary>
public enum EntryDirection
{
    /// <summary>Money leaving this account.</summary>
    Payment = 0,

    /// <summary>Money arriving in this account.</summary>
    Deposit = 1,
}

/// <summary>
/// Creates or edits one register row.
/// </summary>
/// <remarks>
/// The amount is collected as a positive figure plus a direction rather than as a signed
/// number. Microsoft Money's register does the same with its Payment and Deposit columns,
/// and it removes the single most common way to enter a transaction backwards.
/// </remarks>
public sealed partial class TransactionEditorViewModel : DialogViewModel
{
    private readonly RegisterService _register;
    private readonly PayeeService _payees;
    private readonly SuggestionService _suggestions;
    private readonly IModalService _modals;
    private readonly int? _id;
    private readonly int _accountId;

    private IReadOnlyList<SplitDraft> _splits = [];

    private TransactionEditorViewModel(
        RegisterService register,
        PayeeService payees,
        SuggestionService suggestions,
        IModalService modals,
        int accountId,
        IReadOnlyList<Account> transferTargets,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<string> payeeNames,
        Transaction? existing)
    {
        _register = register;
        _payees = payees;
        _suggestions = suggestions;
        _modals = modals;
        _accountId = accountId;
        _id = existing?.Id;

        TransferTargets = [.. transferTargets.Where(a => a.Id != accountId)];
        Categories = categories;
        PayeeNames = payeeNames;

        if (existing is null)
        {
            _date = DateTime.Today;
            return;
        }

        _date = existing.Date.ToDateTime(TimeOnly.MinValue);
        _number = existing.Number;
        _payeeName = existing.Payee?.Name;
        _memo = existing.Memo;
        _direction = existing.Amount.IsPositive ? EntryDirection.Deposit : EntryDirection.Payment;
        _amountText = existing.Amount.Abs().ToString("N", CultureInfo.CurrentCulture);
        _isCleared = existing.ClearedStatus != ClearedStatus.Uncleared;
        _isVoid = existing.IsVoid;
        _isReconciled = existing.ClearedStatus == ClearedStatus.Reconciled;

        if (existing.TransferPeer is not null)
        {
            _transferAccount = TransferTargets.FirstOrDefault(a => a.Id == existing.TransferPeer.AccountId);
        }

        _splits =
        [
            .. existing.Splits.Select(s => new SplitDraft
            {
                CategoryId = s.CategoryId,
                Amount = s.Amount,
                Memo = s.Memo,
            })
        ];

        if (_splits.Count == 1)
        {
            _category = categories.FirstOrDefault(c => c.Id == _splits[0].CategoryId);
        }
    }

    public static TransactionEditorViewModel ForNew(
        RegisterService register,
        PayeeService payees,
        SuggestionService suggestions,
        IModalService modals,
        int accountId,
        IReadOnlyList<Account> transferTargets,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<string> payeeNames) =>
        new(register, payees, suggestions, modals, accountId, transferTargets, categories, payeeNames, null);

    public static TransactionEditorViewModel ForExisting(
        RegisterService register,
        PayeeService payees,
        SuggestionService suggestions,
        IModalService modals,
        int accountId,
        IReadOnlyList<Account> transferTargets,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<string> payeeNames,
        Transaction existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return new TransactionEditorViewModel(
            register, payees, suggestions, modals, accountId, transferTargets, categories, payeeNames, existing);
    }

    public override string Title => _id is null ? "New transaction" : "Edit transaction";

    public IReadOnlyList<Account> TransferTargets { get; }

    public IReadOnlyList<CategoryListItem> Categories { get; }

    public IReadOnlyList<string> PayeeNames { get; }

    public IReadOnlyList<EntryDirection> Directions { get; } =
        [EntryDirection.Payment, EntryDirection.Deposit];

    [ObservableProperty]
    private DateTime _date = DateTime.Today;

    [ObservableProperty]
    private string? _number;

    [ObservableProperty]
    private string? _payeeName;

    [ObservableProperty]
    private string? _memo;

    [ObservableProperty]
    private EntryDirection _direction = EntryDirection.Payment;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private CategoryListItem? _category;

    /// <summary>The category being offered, held until the user accepts it.</summary>
    private CategoryListItem? _suggested;

    /// <summary>Why this category was chosen, or what is being offered and why.</summary>
    [ObservableProperty]
    private string _suggestionText = string.Empty;

    /// <summary>True when there is an offer to accept, as opposed to a filled-in explanation.</summary>
    [ObservableProperty]
    private bool _hasSuggestion;

    [ObservableProperty]
    private Account? _transferAccount;

    [ObservableProperty]
    private bool _isCleared;

    [ObservableProperty]
    private bool _isVoid;

    private bool _isReconciled;

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
        }
    }

    /// <summary>Drops back to a single category, discarding the split lines.</summary>
    [RelayCommand]
    private void ClearSplits()
    {
        _splits = [];
        RefreshCategoryState();
    }

    /// <summary>
    /// Recommends a category once the payee is known, the way Money's register does as soon
    /// as you leave the payee field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the same chain an import does — a rule you wrote, this payee's usual category, a
    /// classifier trained on your history, then the merchant this one most resembles — rather
    /// than the payee's remembered category alone, which is all the register used to consult.
    /// </para>
    /// <para>
    /// The three authoritative sources fill the box; the two guesses only offer. That line is
    /// <see cref="CategorySuggestion.IsCertain" /> rather than a condition written here, so it
    /// is the same everywhere and can be tested without a window.
    /// </para>
    /// <para>
    /// It runs on an existing transaction as well as a new one, but only where nothing is
    /// filed yet. Opening a row to correct its amount should not second-guess a category
    /// chosen deliberately, possibly years ago.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task SuggestCategoryAsync()
    {
        ClearSuggestion();

        if (IsSplit || IsTransfer || string.IsNullOrWhiteSpace(PayeeName))
        {
            return;
        }

        string payeeName = PayeeName;

        // The amount is only ever pre-filled on a new entry: overwriting the amount of a
        // transaction that already exists would be editing the user's data, not helping.
        if (_id is null && string.IsNullOrWhiteSpace(AmountText))
        {
            PayeeListItem? payee = await Task
                .Run(() => _payees.FindByNameAsync(payeeName))
                .ConfigureAwait(true);

            if (payee?.LastAmount is Money last)
            {
                AmountText = last.Abs().ToString("N", CultureInfo.CurrentCulture);
                Direction = last.IsPositive ? EntryDirection.Deposit : EntryDirection.Payment;
            }
        }

        if (Category is not null)
        {
            return;
        }

        var request = new SuggestionRequest(_accountId, payeeName, SignedAmount(), Memo);

        // On a pool thread because the first call after a book opens trains the classifier
        // over every categorized transaction in it. SQLite's async methods are synchronous
        // wrappers, so awaiting this directly would hold the dialog still while it ran.
        // Nothing is shown while it works: a suggestion is a convenience the user has not
        // asked for, and a modal bar over it would make it feel like a step they must wait on.
        CategorySuggestion suggestion = await Task
            .Run(() => _suggestions.SuggestAsync(request))
            .ConfigureAwait(true);

        // The user may have typed on while that ran; a suggestion for a payee they have moved
        // on from, or a category they have since chosen, is worse than none.
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
            // Suggested a category this book no longer has, which an archived category will do.
            return;
        }

        if (suggestion.IsCertain)
        {
            Category = category;
            SuggestionText = suggestion.Describe();
            return;
        }

        // A guess: shown, not applied.
        _suggested = category;
        SuggestionText = $"Suggested: {category.FullName} — {suggestion.Describe()}";
        HasSuggestion = true;
    }

    /// <summary>Takes the offered category.</summary>
    [RelayCommand]
    private void UseSuggestion()
    {
        if (_suggested is null)
        {
            return;
        }

        // Captured first: assigning Category clears the offer through OnCategoryChanged.
        CategoryListItem accepted = _suggested;

        Category = accepted;
        ClearSuggestion();
    }

    private void ClearSuggestion()
    {
        _suggested = null;
        SuggestionText = string.Empty;
        HasSuggestion = false;
    }

    /// <summary>The amount as the rules see it, which need a sign to match an amount rule.</summary>
    private Money SignedAmount()
    {
        if (!Money.TryParse(AmountText, CultureInfo.CurrentCulture, out Money amount))
        {
            return Money.Zero;
        }

        return Direction == EntryDirection.Payment ? -amount.Abs() : amount.Abs();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

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
            // The amount may have moved since the split lines were written.
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
            Id = _id,
            AccountId = _accountId,
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

        IsBusy = true;

        try
        {
            SavedId = await _register.SaveAsync(draft).ConfigureAwait(true);
            Close(true);
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Id of the row just written, so the register can reselect it.</summary>
    public int? SavedId { get; private set; }

    /// <summary>
    /// Keeps a reconciled row reconciled unless the user actually unticks it, so saving an
    /// unrelated edit does not quietly undo a completed reconciliation.
    /// </summary>
    private ClearedStatus ResolveClearedStatus()
    {
        if (!IsCleared)
        {
            return ClearedStatus.Uncleared;
        }

        return _isReconciled ? ClearedStatus.Reconciled : ClearedStatus.Cleared;
    }

    private bool TryReadAmount(out Money amount)
    {
        if (!Money.TryParse(AmountText, CultureInfo.CurrentCulture, out Money entered))
        {
            amount = Money.Zero;
            return false;
        }

        // The direction control owns the sign, so a leading minus typed into the box as well
        // cannot flip the transaction back the other way.
        amount = Direction == EntryDirection.Payment ? entered.Abs().Negated() : entered.Abs();
        return true;
    }

    private void RefreshCategoryState()
    {
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(IsTransfer));
        OnPropertyChanged(nameof(CanPickCategory));
        OnPropertyChanged(nameof(CategorySummary));
    }

    partial void OnCategoryChanged(CategoryListItem? value)
    {
        // Whatever was said about the old category no longer describes this one. The
        // recommendation sets the text after assigning Category, so its own explanation
        // survives; anything the user picks by hand clears it.
        ClearSuggestion();
        RefreshCategoryState();
    }

    partial void OnTransferAccountChanged(Account? value)
    {
        if (value is not null)
        {
            _splits = [];
            Category = null;
        }

        RefreshCategoryState();
    }
}
