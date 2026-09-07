using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Entities;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>
/// Manages the rules that categorize transactions on import.
/// </summary>
/// <remarks>
/// Order matters here in a way it does not on the other list screens: the first rule that
/// matches wins, so moving one above another changes what both of them do. That is why the
/// list is ordered manually rather than sorted by name.
/// </remarks>
public sealed partial class RulesPageViewModel : PageViewModel
{
    private readonly CategorizationRuleService _rules;
    private readonly CategoryService _categories;
    private readonly PayeeService _payees;
    private readonly AccountService _accounts;
    private readonly IModalService _modals;
    private readonly IDialogService _dialogs;

    public RulesPageViewModel(
        CategorizationRuleService rules,
        CategoryService categories,
        PayeeService payees,
        AccountService accounts,
        IModalService modals,
        IDialogService dialogs)
    {
        _rules = rules;
        _categories = categories;
        _payees = payees;
        _accounts = accounts;
        _modals = modals;
        _dialogs = dialogs;
    }

    public override string Title => "Categorization rules";

    public override AppSection Section => AppSection.Banking;

    public override HelpTopic HelpTopic => HelpTopic.Categorizing;

    public ObservableCollection<RuleListItem> Items { get; } = [];

    [ObservableProperty]
    private RuleListItem? _selected;

    [ObservableProperty]
    private bool _isEmpty = true;

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Rules",
            Links =
            [
                new TaskLink { Text = "New rule", Execute = () => NewCommand.Execute(null) },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IReadOnlyList<RuleListItem>? all = await RunBusyAsync(
            "Reading the rules",
            (_, token) => _rules.GetAllAsync(cancellationToken: token)).ConfigureAwait(true);

        if (all is null)
        {
            return;
        }

        int? previous = Selected?.Id;

        Items.Clear();
        foreach (RuleListItem item in all)
        {
            Items.Add(item);
        }

        IsEmpty = Items.Count == 0;
        Selected = Items.FirstOrDefault(i => i.Id == previous);
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        RuleEditorViewModel editor = RuleEditorViewModel.ForNew(
            _rules,
            await _categories.GetAllAsync().ConfigureAwait(true),
            await _payees.GetAllAsync().ConfigureAwait(true),
            await _accounts.GetAllAsync().ConfigureAwait(true));

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task EditAsync(RuleListItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        RuleEditorViewModel editor = RuleEditorViewModel.ForExisting(
            _rules,
            await _categories.GetAllAsync().ConfigureAwait(true),
            await _payees.GetAllAsync().ConfigureAwait(true),
            await _accounts.GetAllAsync().ConfigureAwait(true),
            item);

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(RuleListItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        await _rules.SetEnabledAsync(item.Id, !item.IsEnabled).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task MoveUpAsync() => MoveAsync(-1);

    [RelayCommand]
    private Task MoveDownAsync() => MoveAsync(1);

    [RelayCommand]
    private async Task DeleteAsync(RuleListItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Delete rule",
            $"Delete \"{item.Name}\"?\n\nTransactions it has already categorized keep their categories; only future imports change."))
        {
            return;
        }

        try
        {
            await _rules.DeleteAsync(item.Id).ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Delete rule", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task MoveAsync(int offset)
    {
        if (Selected is null)
        {
            return;
        }

        int id = Selected.Id;

        await _rules.MoveAsync(id, offset).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);

        Selected = Items.FirstOrDefault(i => i.Id == id);
    }
}
