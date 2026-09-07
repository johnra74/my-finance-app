using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>One line of a split, as the split editor edits it.</summary>
public sealed partial class SplitRowViewModel : ObservableObject
{
    public SplitRowViewModel(IReadOnlyList<CategoryListItem> categories)
    {
        Categories = categories;
    }

    public IReadOnlyList<CategoryListItem> Categories { get; }

    [ObservableProperty]
    private CategoryListItem? _category;

    /// <summary>
    /// Signed, in the same direction as the transaction total, and held as text so a
    /// half-typed figure is not read as a number.
    /// </summary>
    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private string? _memo;

    /// <summary>The parsed amount, or zero while the text is not yet a number.</summary>
    public Money Amount =>
        Money.TryParse(AmountText, CultureInfo.CurrentCulture, out Money value) ? value : Money.Zero;

    public bool IsAmountValid =>
        string.IsNullOrWhiteSpace(AmountText)
        || Money.TryParse(AmountText, CultureInfo.CurrentCulture, out _);

    partial void OnAmountTextChanged(string value)
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(IsAmountValid));
        AmountChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised whenever the parsed amount may have moved, so the total can retally.</summary>
    public event EventHandler? AmountChanged;
}

/// <summary>
/// Spreads one transaction across several categories.
/// </summary>
/// <remarks>
/// The editor will not close until the lines add up to the transaction total. Allowing a
/// mismatch would break the invariant every report depends on — that a transaction's splits
/// sum to its amount — and the discrepancy would surface much later as a report that does
/// not agree with the register.
/// </remarks>
public sealed partial class SplitEditorViewModel : DialogViewModel
{
    public SplitEditorViewModel(
        Money total,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<SplitDraft> existing)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(existing);

        Total = total;
        Categories = categories;
        Rows = [];

        foreach (SplitDraft split in existing)
        {
            Rows.Add(CreateRow(
                categories.FirstOrDefault(c => c.Id == split.CategoryId),
                split.Amount,
                split.Memo));
        }

        // A brand-new split starts with two lines: one carrying the whole amount and one
        // empty, which is the shape of the edit almost everybody is about to make.
        if (Rows.Count == 0)
        {
            Rows.Add(CreateRow(null, total, null));
            Rows.Add(CreateRow(null, Money.Zero, null));
        }

        Rows.CollectionChanged += (_, _) => Retally();
        Retally();
    }

    public Money Total { get; }

    public IReadOnlyList<CategoryListItem> Categories { get; }

    public ObservableCollection<SplitRowViewModel> Rows { get; }

    public override string Title => "Split transaction";

    /// <summary>The result, valid only once the dialog was accepted.</summary>
    public IReadOnlyList<SplitDraft> Result { get; private set; } = [];

    [ObservableProperty]
    private SplitRowViewModel? _selectedRow;

    [ObservableProperty]
    private Money _assigned;

    [ObservableProperty]
    private Money _unassigned;

    public bool IsBalanced => Unassigned.IsZero;

    public string TotalText => Total.ToAccountingString(CultureInfo.CurrentCulture);

    public string AssignedText => Assigned.ToAccountingString(CultureInfo.CurrentCulture);

    public string UnassignedText => Unassigned.ToAccountingString(CultureInfo.CurrentCulture);

    [RelayCommand]
    private void AddRow()
    {
        SplitRowViewModel row = CreateRow(null, Unassigned, null);
        Rows.Add(row);
        SelectedRow = row;
    }

    [RelayCommand]
    private void RemoveRow(SplitRowViewModel? row)
    {
        row ??= SelectedRow;

        if (row is not null && Rows.Count > 1)
        {
            row.AmountChanged -= OnRowAmountChanged;
            Rows.Remove(row);
        }
    }

    /// <summary>Drops whatever is left over onto the selected line, so it balances in one click.</summary>
    [RelayCommand]
    private void AssignRemainder()
    {
        SplitRowViewModel? row = SelectedRow ?? Rows.LastOrDefault();
        if (row is null || Unassigned.IsZero)
        {
            return;
        }

        row.AmountText = (row.Amount + Unassigned).ToString("N", CultureInfo.CurrentCulture);
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        List<SplitRowViewModel> filled =
        [
            .. Rows.Where(r => !r.Amount.IsZero || r.Category is not null)
        ];

        if (filled.Count == 0)
        {
            ErrorMessage = "Add at least one line, or cancel to leave the transaction uncategorized.";
            return;
        }

        if (Rows.Any(r => !r.IsAmountValid))
        {
            ErrorMessage = "One of the amounts is not a number.";
            return;
        }

        if (!IsBalanced)
        {
            ErrorMessage =
                $"The lines add up to {AssignedText} but the transaction is {TotalText}. {UnassignedText} is still unassigned.";
            return;
        }

        Result =
        [
            .. filled.Select(r => new SplitDraft
            {
                CategoryId = r.Category?.Id,
                Amount = r.Amount,
                Memo = r.Memo,
            })
        ];

        Close(true);
    }

    private SplitRowViewModel CreateRow(CategoryListItem? category, Money amount, string? memo)
    {
        var row = new SplitRowViewModel(Categories)
        {
            Category = category,
            AmountText = amount.IsZero ? string.Empty : amount.ToString("N", CultureInfo.CurrentCulture),
            Memo = memo,
        };

        row.AmountChanged += OnRowAmountChanged;
        return row;
    }

    private void OnRowAmountChanged(object? sender, EventArgs e) => Retally();

    private void Retally()
    {
        Assigned = Money.Sum(Rows.Select(r => r.Amount));
        Unassigned = Total - Assigned;

        OnPropertyChanged(nameof(IsBalanced));
        OnPropertyChanged(nameof(AssignedText));
        OnPropertyChanged(nameof(UnassignedText));
    }
}
