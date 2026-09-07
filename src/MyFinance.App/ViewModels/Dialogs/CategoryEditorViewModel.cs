using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>Creates a category or edits an existing one.</summary>
public sealed partial class CategoryEditorViewModel : DialogViewModel
{
    private readonly CategoryService _categories;
    private readonly int? _id;

    private CategoryEditorViewModel(
        CategoryService categories,
        IReadOnlyList<Category> parents,
        CategoryListItem? existing)
    {
        _categories = categories;
        _id = existing?.Id;

        // A category cannot be its own parent, and the two-level rule means anything with
        // children can only ever be a heading.
        Parents = [.. parents.Where(p => existing is null || p.Id != existing.Id)];

        if (existing is null)
        {
            return;
        }

        _name = existing.Category.Name;
        _isIncome = existing.Kind == CategoryKind.Income;
        _isTaxRelated = existing.Category.IsTaxRelated;
        _parent = Parents.FirstOrDefault(p => p.Id == existing.Category.ParentId);
    }

    public static CategoryEditorViewModel ForNew(
        CategoryService categories,
        IReadOnlyList<Category> parents,
        Category? preselectedParent = null)
    {
        var model = new CategoryEditorViewModel(categories, parents, null);

        if (preselectedParent is not null)
        {
            model.Parent = model.Parents.FirstOrDefault(p => p.Id == preselectedParent.Id);
        }

        return model;
    }

    public static CategoryEditorViewModel ForExisting(
        CategoryService categories,
        IReadOnlyList<Category> parents,
        CategoryListItem existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return new CategoryEditorViewModel(categories, parents, existing);
    }

    public override string Title => _id is null ? "New category" : "Category details";

    public IReadOnlyList<Category> Parents { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private Category? _parent;

    [ObservableProperty]
    private bool _isIncome;

    [ObservableProperty]
    private bool _isTaxRelated;

    /// <summary>
    /// A subcategory takes its heading's side of the books, so the choice is only offered on
    /// a top-level category.
    /// </summary>
    public bool CanChooseKind => Parent is null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsBusy = true;

        CategoryKind kind = IsIncome ? CategoryKind.Income : CategoryKind.Expense;

        try
        {
            if (_id is int id)
            {
                await _categories.UpdateAsync(id, Name, Parent?.Id, kind, IsTaxRelated)
                    .ConfigureAwait(true);
            }
            else
            {
                await _categories.CreateAsync(Name, Parent?.Id, kind, IsTaxRelated)
                    .ConfigureAwait(true);
            }

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

    partial void OnParentChanged(Category? value) => OnPropertyChanged(nameof(CanChooseKind));
}
