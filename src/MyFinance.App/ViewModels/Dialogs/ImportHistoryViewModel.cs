using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>One past import, as the history list shows it.</summary>
public sealed class ImportHistoryRowViewModel
{
    public required ImportHistoryEntry Entry { get; init; }

    public int Id => Entry.Id;

    public string ImportedText =>
        Entry.Batch.ImportedUtc.LocalDateTime.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);

    public string FileText => Entry.Batch.SourceFileName ?? "(no file name)";

    public string AccountText => Entry.AccountName ?? "(account deleted)";

    public string PeriodText => Entry.Batch.PeriodStart is DateOnly from && Entry.Batch.PeriodEnd is DateOnly to
        ? $"{from:d MMM yyyy} – {to:d MMM yyyy}"
        : "—";

    public string CountText => Entry.IsReverted
        ? $"{Entry.Batch.TransactionsAdded} added, undone"
        : $"{Entry.Batch.TransactionsAdded} added";

    public bool CanRevert => Entry.CanRevert;

    public bool IsReverted => Entry.IsReverted;
}

/// <summary>
/// Lists past imports and lets one be undone as a unit.
/// </summary>
/// <remarks>
/// The batch record exists so a bad import can be backed out in one action. Without a screen
/// that reaches it, that record is bookkeeping nobody can act on, and recovering from a
/// three-hundred-row mistake means deleting rows by hand.
/// </remarks>
public sealed partial class ImportHistoryViewModel : DialogViewModel
{
    private readonly ImportService _import;
    private readonly IDialogService _dialogs;

    public ImportHistoryViewModel(ImportService import, IDialogService dialogs)
    {
        _import = import;
        _dialogs = dialogs;
    }

    public override string Title => "Import history";

    public ObservableCollection<ImportHistoryRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private ImportHistoryRowViewModel? _selected;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Set when something was undone, so the caller knows to refresh.</summary>
    public bool ChangedAnything { get; private set; }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;

        try
        {
            IReadOnlyList<ImportHistoryEntry> history = await _import.GetHistoryAsync()
                .ConfigureAwait(true);

            int? previous = Selected?.Id;

            Rows.Clear();
            foreach (ImportHistoryEntry entry in history)
            {
                Rows.Add(new ImportHistoryRowViewModel { Entry = entry });
            }

            IsEmpty = Rows.Count == 0;
            Selected = Rows.FirstOrDefault(r => r.Id == previous);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RevertAsync(ImportHistoryRowViewModel? row)
    {
        row ??= Selected;

        if (row is null || !row.CanRevert)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Undo import",
            $"Remove the {row.Entry.RemainingTransactions} transaction(s) this import added to {row.AccountText}?\n\nPayees and the payee shortcuts it learned are kept. This cannot itself be undone."))
        {
            return;
        }

        ErrorMessage = null;
        int batchId = row.Id;

        try
        {
            int? removed = await RunBusyAsync(
                "Undoing the import",
                (_, token) => _import.RevertAsync(batchId, token)).ConfigureAwait(true);

            // Null means it was stopped part-way; the transaction rolled back and nothing
            // downstream should be told the book changed.
            ChangedAnything = removed is not null;
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Done() => Close(ChangedAnything);
}
