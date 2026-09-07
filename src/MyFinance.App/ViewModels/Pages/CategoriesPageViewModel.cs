using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Entities;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>Manages the chart of categories every report groups by.</summary>
public sealed partial class CategoriesPageViewModel : PageViewModel
{
    private readonly CategoryService _categories;
    private readonly IModalService _modals;
    private readonly IDialogService _dialogs;

    public CategoriesPageViewModel(
        CategoryService categories,
        IModalService modals,
        IDialogService dialogs)
    {
        _categories = categories;
        _modals = modals;
        _dialogs = dialogs;
    }

    public override string Title => "Categories";

    public override AppSection Section => AppSection.Banking;

    public override HelpTopic HelpTopic => HelpTopic.Categorizing;

    public ObservableCollection<CategoryListItem> Items { get; } = [];

    [ObservableProperty]
    private CategoryListItem? _selected;

    [ObservableProperty]
    private bool _showArchived;

    [ObservableProperty]
    private bool _isEmpty = true;

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Categories",
            Links =
            [
                new TaskLink { Text = "New category", Execute = () => NewCommand.Execute(null) },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        bool showArchived = ShowArchived;

        IReadOnlyList<CategoryListItem>? all = await RunBusyAsync(
            "Reading the categories",
            (_, token) => _categories.GetAllAsync(showArchived, cancellationToken: token))
            .ConfigureAwait(true);

        if (all is null)
        {
            return;
        }

        int? previous = Selected?.Id;

        Items.Clear();
        foreach (CategoryListItem item in all)
        {
            Items.Add(item);
        }

        IsEmpty = Items.Count == 0;
        Selected = Items.FirstOrDefault(i => i.Id == previous);
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        IReadOnlyList<Category> parents = await _categories.GetParentsAsync().ConfigureAwait(true);

        // Adding while a heading is selected pre-selects it as the parent, which is what you
        // almost always want when you noticed the gap while looking at that heading.
        Category? preselected = Selected is null
            ? null
            : parents.FirstOrDefault(p => p.Id == (Selected.Category.ParentId ?? Selected.Id));

        CategoryEditorViewModel editor =
            CategoryEditorViewModel.ForNew(_categories, parents, preselected);

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task EditAsync(CategoryListItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        IReadOnlyList<Category> parents = await _categories.GetParentsAsync().ConfigureAwait(true);
        CategoryEditorViewModel editor =
            CategoryEditorViewModel.ForExisting(_categories, parents, item);

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleArchivedAsync(CategoryListItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        await _categories.SetArchivedAsync(item.Id, !item.IsArchived).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(CategoryListItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Delete category",
            $"Delete \"{item.FullName}\"?\n\nThis cannot be undone. Categories that have been used cannot be deleted — archive those instead so their history stays readable."))
        {
            return;
        }

        try
        {
            await _categories.DeleteAsync(item.Id).ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Delete category", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnShowArchivedChanged(bool value) => _ = RefreshAsync();
}
