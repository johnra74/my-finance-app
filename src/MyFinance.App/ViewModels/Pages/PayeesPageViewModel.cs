using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>Manages the payee list the register autocompletes from.</summary>
public sealed partial class PayeesPageViewModel : PageViewModel
{
    private readonly PayeeService _payees;
    private readonly IDialogService _dialogs;

    public PayeesPageViewModel(PayeeService payees, IDialogService dialogs)
    {
        _payees = payees;
        _dialogs = dialogs;
    }

    public override string Title => "Payees";

    public override AppSection Section => AppSection.Banking;

    public override HelpTopic HelpTopic => HelpTopic.Categorizing;

    public ObservableCollection<PayeeListItem> Items { get; } = [];

    [ObservableProperty]
    private PayeeListItem? _selected;

    /// <summary>Second selection used by merge, so two payees can be combined into one.</summary>
    [ObservableProperty]
    private PayeeListItem? _mergeTarget;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Payees",
            Links = [new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) }],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IReadOnlyList<PayeeListItem>? all = await RunBusyAsync(
            "Reading the payees",
            (_, token) => _payees.GetAllAsync(cancellationToken: token)).ConfigureAwait(true);

        if (all is null)
        {
            return;
        }

        int? previous = Selected?.Id;

        Items.Clear();
        foreach (PayeeListItem item in all)
        {
            Items.Add(item);
        }

        IsEmpty = Items.Count == 0;
        Selected = Items.FirstOrDefault(i => i.Id == previous);
        MergeTarget = null;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            return;
        }

        try
        {
            await _payees.FindOrCreateAsync(NewName).ConfigureAwait(true);
            NewName = string.Empty;
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Add payee", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RenameAsync(PayeeListItem? item)
    {
        item ??= Selected;
        if (item is null || string.IsNullOrWhiteSpace(NewName))
        {
            return;
        }

        try
        {
            await _payees.RenameAsync(item.Id, NewName).ConfigureAwait(true);
            NewName = string.Empty;
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Rename payee", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(PayeeListItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        if (!_dialogs.Confirm("Delete payee", $"Delete \"{item.Name}\"?"))
        {
            return;
        }

        try
        {
            await _payees.DeleteAsync(item.Id).ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Delete payee", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Folds the selected payee into the merge target. The fix for the duplicates a bank
    /// download inevitably creates — "SQ *BLUE BOTTLE 1234" alongside "Blue Bottle Coffee".
    /// </summary>
    [RelayCommand]
    private async Task MergeAsync()
    {
        if (Selected is null || MergeTarget is null || Selected.Id == MergeTarget.Id)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Merge payees",
            $"Move all {Selected.UseCount} transaction{(Selected.UseCount == 1 ? string.Empty : "s")} from \"{Selected.Name}\" onto \"{MergeTarget.Name}\" and delete \"{Selected.Name}\"?\n\nThe old name is kept as an alias so future imports land on the surviving payee."))
        {
            return;
        }

        try
        {
            await _payees.MergeAsync(Selected.Id, MergeTarget.Id).ConfigureAwait(true);
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Merge payees", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnSelectedChanged(PayeeListItem? value) =>
        NewName = value?.Name ?? string.Empty;
}
