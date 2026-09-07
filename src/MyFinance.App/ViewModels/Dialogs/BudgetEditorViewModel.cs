using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Budgeting;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>Sets the budget for one category and month.</summary>
public sealed partial class BudgetEditorViewModel : DialogViewModel
{
    private readonly BudgetService _budgets;
    private readonly DateOnly _month;

    private BudgetEditorViewModel(
        BudgetService budgets,
        IReadOnlyList<CategoryListItem> categories,
        DateOnly month,
        BudgetLineResult? existing)
    {
        _budgets = budgets;
        _month = month;

        Categories = categories;

        if (existing is null)
        {
            return;
        }

        _selectedCategory = categories.FirstOrDefault(c => c.Id == existing.CategoryId);
        _amountText = existing.Budgeted.ToString("N", CultureInfo.CurrentCulture);
        _rollsOver = existing.RollsOver;
    }

    public static BudgetEditorViewModel For(
        BudgetService budgets,
        IReadOnlyList<CategoryListItem> categories,
        DateOnly month,
        BudgetLineResult? existing) =>
        new(budgets, categories, month, existing);

    public override string Title => $"Budget for {_month:MMMM yyyy}";

    public IReadOnlyList<CategoryListItem> Categories { get; }

    [ObservableProperty]
    private CategoryListItem? _selectedCategory;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private bool _rollsOver;

    public string RolloverHint =>
        "Anything left unspent is added to next month's figure. An overspend is not carried forward — next month starts from what you set for it.";

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (SelectedCategory is null)
        {
            ErrorMessage = "Choose a category to budget for.";
            return;
        }

        if (!Money.TryParse(AmountText, CultureInfo.CurrentCulture, out Money amount))
        {
            ErrorMessage = "The amount is not a number.";
            return;
        }

        IsBusy = true;

        try
        {
            await _budgets.SetAsync(new BudgetDraft
            {
                CategoryId = SelectedCategory.Id,
                PeriodStart = _month,
                Amount = amount,
                RollsOver = RollsOver,
            }).ConfigureAwait(true);

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
}
