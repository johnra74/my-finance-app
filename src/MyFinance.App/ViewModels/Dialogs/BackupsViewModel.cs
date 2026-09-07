using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Data.Security;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>
/// Backups: what there is, taking another, and how they are kept.
/// </summary>
/// <remarks>
/// Restoring is deliberately not offered here. It would mean replacing the file this very
/// session is reading and writing, so it belongs on the opening screen, where no book is
/// open. The dialog says so rather than leaving somebody hunting for it.
/// </remarks>
public sealed partial class BackupsViewModel : DialogViewModel
{
    private readonly BookBackupService _backups;
    private readonly IBookSession _session;
    private readonly IDialogService _dialogs;

    public BackupsViewModel(
        BookBackupService backups,
        IBookSession session,
        IDialogService dialogs)
    {
        _backups = backups;
        _session = session;
        _dialogs = dialogs;
    }

    public override string Title => "Backups";

    public ObservableCollection<BackupEntry> Entries { get; } = [];

    [ObservableProperty]
    private BackupEntry? _selected;

    [ObservableProperty]
    private bool _automatic = true;

    [ObservableProperty]
    private string _keepText = "10";

    [ObservableProperty]
    private string _folder = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>
    /// Set while the panel is filling itself in from stored preferences.
    /// </summary>
    /// <remarks>
    /// Without it, loading a value into a bound property looks exactly like the user
    /// changing it, and the panel saves what it has just read — a loop that at best wastes a
    /// write and at worst persists a half-loaded state.
    /// </remarks>
    private bool _loading;

    public string BookName => _session.BookName ?? "this book";

    /// <summary>
    /// Why a backup is worth taking, in the words that make somebody actually take one.
    /// </summary>
    /// <remarks>
    /// Not decoration. A book is encrypted with no recovery path, and the sidecar holding
    /// its salt is as essential as the password. People who have not been told that copy the
    /// wrong file and find out years later.
    /// </remarks>
    public string Explanation =>
        "A backup holds both halves of the book — the encrypted data and the small key file "
        + "beside it — because both are needed to open it. It is protected by the same password, "
        + "so it is exactly as safe to keep on a memory stick or in cloud storage as the book itself.";

    public string RestoreHint =>
        "To restore one, close this book and choose “Restore from a backup…” on the opening screen. "
        + "It cannot be done while the book is open.";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_session.Book is not Book book)
        {
            return;
        }

        _loading = true;

        try
        {
            BackupPreferences preferences = await _backups.GetPreferencesAsync().ConfigureAwait(true);

            Automatic = preferences.Automatic;
            KeepText = preferences.EffectiveKeep.ToString(CultureInfo.CurrentCulture);
            Folder = await _backups.GetDirectoryAsync(book).ConfigureAwait(true);

            Entries.Clear();

            foreach (BackupEntry entry in await _backups.ListAsync(book).ConfigureAwait(true))
            {
                Entries.Add(entry);
            }

            IsEmpty = Entries.Count == 0;
            StatusText = Entries.Count == 0
                ? "No backups yet."
                : $"{Entries.Count} backup{(Entries.Count == 1 ? string.Empty : "s")}, newest first.";
        }
        finally
        {
            _loading = false;
        }
    }

    [RelayCommand]
    private Task BackUpNowAsync() => RunBackupAsync(null);

    /// <summary>Backs up somewhere of the user's choosing, and leaves that copy alone after.</summary>
    [RelayCommand]
    private Task BackUpToAsync()
    {
        string suggestion = BackupService.SuggestFileName(BookName, DateTimeOffset.Now);
        string? destination = _dialogs.PickBackupDestination(suggestion);

        return destination is null ? Task.CompletedTask : RunBackupAsync(destination);
    }

    private async Task RunBackupAsync(string? destination)
    {
        if (_session.Book is not Book book)
        {
            return;
        }

        ErrorMessage = null;

        try
        {
            BackupEntry? entry = await RunBusyAsync(
                "Backing up",
                (report, token) => _backups.BackUpNowAsync(book, destination, report, token))
                .ConfigureAwait(true);

            if (entry is null)
            {
                StatusText = WasCancelled ? "Backup stopped. Nothing was written." : StatusText;
                return;
            }

            await RefreshAsync().ConfigureAwait(true);

            StatusText = $"Backed up to {entry.FileName} ({entry.SizeText}).";
        }
        catch (BackupException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        string? chosen = _dialogs.PickBackupFolder(Folder);

        if (chosen is null)
        {
            return;
        }

        await SaveAsync(new BackupPreferences
        {
            Automatic = Automatic,
            Keep = ParsedKeep(),
            Directory = chosen,
        }).ConfigureAwait(true);
    }

    /// <summary>Puts backups back beside the book.</summary>
    [RelayCommand]
    private Task UseDefaultFolderAsync() => SaveAsync(new BackupPreferences
    {
        Automatic = Automatic,
        Keep = ParsedKeep(),
        Directory = null,
    });

    [RelayCommand]
    private async Task DeleteAsync(BackupEntry? entry)
    {
        entry ??= Selected;

        if (entry is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Delete backup",
            $"Delete {entry.FileName}?\n\nIt is taken from {entry.CreatedText} and cannot be got back."))
        {
            return;
        }

        try
        {
            File.Delete(entry.Path);
        }
        catch (IOException ex)
        {
            ErrorMessage = $"That backup could not be deleted: {ex.Message}";
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorMessage = $"That backup could not be deleted: {ex.Message}";
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Shows the folder, which is how somebody copies a backup somewhere safe.</summary>
    [RelayCommand]
    private void OpenFolder() => _dialogs.OpenFolder(Folder);

    [RelayCommand]
    private void Done() => Close(true);

    private int ParsedKeep() =>
        int.TryParse(KeepText, CultureInfo.CurrentCulture, out int keep) ? keep : BackupPreferences.Default.Keep;

    private async Task SaveAsync(BackupPreferences preferences)
    {
        await _backups.SavePreferencesAsync(preferences).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    // Settings save as they are changed: a preferences panel with its own Save button is one
    // more thing to forget, and forgetting this one means no backups.
    partial void OnAutomaticChanged(bool value) => SaveCurrent();

    partial void OnKeepTextChanged(string value) => SaveCurrent();

    private void SaveCurrent()
    {
        if (_loading)
        {
            return;
        }

        _ = SaveAsync(new BackupPreferences
        {
            Automatic = Automatic,
            Keep = ParsedKeep(),
            Directory = string.IsNullOrWhiteSpace(Folder) ? null : Folder,
        });
    }
}
