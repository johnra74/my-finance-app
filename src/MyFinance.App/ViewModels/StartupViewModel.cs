using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Security;
using MyFinance.Core.Diagnostics;
using MyFinance.Core.Help;
using MyFinance.Data.Security;

namespace MyFinance.App.ViewModels;

/// <summary>Which panel the startup window is showing.</summary>
public enum StartupMode
{
    Welcome,
    Unlock,
    Create,
}

/// <summary>
/// Drives the pre-shell experience: pick a book, unlock it, or create a new one.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IBookSession _session;
    private readonly IDialogService _dialogs;
    private readonly IRecentBooksService _recentBooks;
    private readonly DiagnosticSink? _diagnostics;
    private readonly IHelpService? _help;
    private readonly IModalService? _modals;

    public StartupViewModel(
        IBookSession session,
        IDialogService dialogs,
        IRecentBooksService recentBooks,
        DiagnosticSink? diagnostics = null,
        IHelpService? help = null,
        IModalService? modals = null)
    {
        _session = session;
        _dialogs = dialogs;
        _recentBooks = recentBooks;
        _diagnostics = diagnostics;
        _help = help;
        _modals = modals;

        RecentBooks = new ObservableCollection<RecentBook>(
            _recentBooks.GetRecent().Where(b => b.StillExists));
    }

    /// <summary>Raised once a book is open, so the window can hand over to the shell.</summary>
    public event EventHandler? Unlocked;

    /// <summary>
    /// Opens the documentation at the page about creating and opening a book.
    /// </summary>
    /// <remarks>
    /// Help has to reach this screen, not only the shell. The questions with the worst
    /// consequences — what happens if I forget the password, what is the second file, can I
    /// restore this backup — are all asked here, before there is a book to open Help from.
    /// </remarks>
    [RelayCommand]
    private void Help() => _help?.Open(HelpTopic.FirstBook);

    /// <summary>
    /// Shows the version, the licence and the third-party notices.
    /// </summary>
    /// <remarks>
    /// Here as well as in the shell, because reading what the executable is made of must not
    /// require a password. The person checking that is not necessarily the person who owns
    /// the book.
    /// </remarks>
    [RelayCommand]
    private void About()
    {
        if (_help is not null)
        {
            _modals?.Show(new Dialogs.AboutViewModel(_help));
        }
    }

    public ObservableCollection<RecentBook> RecentBooks { get; }

    [ObservableProperty]
    private StartupMode _mode = StartupMode.Welcome;

    [ObservableProperty]
    private string? _selectedPath;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>File name of the book being unlocked, for the prompt.</summary>
    public string SelectedBookName =>
        SelectedPath is null ? string.Empty : Path.GetFileNameWithoutExtension(SelectedPath);

    partial void OnSelectedPathChanged(string? value) => OnPropertyChanged(nameof(SelectedBookName));

    partial void OnModeChanged(StartupMode value) => ErrorMessage = null;

    // -- Welcome ------------------------------------------------------------------------

    [RelayCommand]
    private void ChooseRecent(RecentBook? book)
    {
        if (book is null)
        {
            return;
        }

        if (!book.StillExists)
        {
            _recentBooks.Forget(book.DatabasePath);
            RecentBooks.Remove(book);
            ErrorMessage = $"'{book.DisplayName}' is no longer where it used to be.";
            return;
        }

        SelectedPath = book.DatabasePath;
        Mode = StartupMode.Unlock;
    }

    [RelayCommand]
    private void BrowseForBook()
    {
        string? path = _dialogs.PickBookToOpen();
        if (path is null)
        {
            return;
        }

        SelectedPath = path;
        Mode = StartupMode.Unlock;
    }

    /// <summary>
    /// Restores a backup and then opens it.
    /// </summary>
    /// <remarks>
    /// Offered here, before any book is open, because that is the only moment a restore is
    /// safe. Replacing the file underneath a book that is already open would leave the
    /// running session writing into a database that no longer exists.
    /// </remarks>
    [RelayCommand]
    private void RestoreFromBackup()
    {
        string? archive = _dialogs.PickBackupToRestore();

        if (archive is null)
        {
            return;
        }

        BackupEntry entry;

        try
        {
            entry = BackupService.Inspect(archive);
        }
        catch (BackupException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        string? destination = _dialogs.PickNewBookLocation();

        if (destination is null)
        {
            return;
        }

        // Restoring over the book it came from is the normal case after a mistake, so it is
        // allowed — but only once the user has said so about this particular file.
        bool exists = BookFileService.Exists(destination);

        if (exists && !_dialogs.Confirm(
            "Restore",
            $"There is already a book at {Path.GetFileName(destination)}.\n\n"
            + "Restoring will replace it completely, and whatever is in it now will be gone. Continue?"))
        {
            return;
        }

        try
        {
            BackupService.Restore(archive, destination, overwrite: exists);
        }
        catch (BackupException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        _dialogs.ShowInformation(
            "Restore",
            $"The backup from {entry.CreatedText} was restored.\n\n"
            + "It opens with the password it had when the backup was taken.");

        SelectedPath = destination;
        ErrorMessage = null;
        Mode = StartupMode.Unlock;
    }

    [RelayCommand]
    private void StartCreate()
    {
        SelectedPath = null;
        Mode = StartupMode.Create;
    }

    [RelayCommand]
    private void BackToWelcome() => Mode = StartupMode.Welcome;

    // -- Unlock -------------------------------------------------------------------------

    /// <summary>
    /// Unlocks the selected book.
    /// </summary>
    /// <remarks>
    /// The password arrives as a parameter straight from the PasswordBox rather than being
    /// held in a bindable property, so it is never copied into an observable string that
    /// would linger on the managed heap for the lifetime of the view model.
    /// </remarks>
    [RelayCommand]
    private async Task UnlockAsync(string? password)
    {
        if (SelectedPath is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Enter your password.";
            return;
        }

        ErrorMessage = null;
        IsBusy = true;

        try
        {
            string path = SelectedPath;

            // Argon2id is deliberately slow; keep it off the UI thread.
            await Task.Run(() => _session.Open(path, password)).ConfigureAwait(true);

            _recentBooks.Remember(path);

            // A tag, never the path — see BookTag. Enough to tell one book's failures from
            // another's without saying anything about either.
            _diagnostics?.BookOpened(path, BookSchema.Current);
            _diagnostics?.Breadcrumb(Operation.OpenBook);

            Unlocked?.Invoke(this, EventArgs.Empty);
        }
        catch (BookUpgradeRequiredException upgrade)
        {
            if (await TryUpgradeAsync(path: SelectedPath, password, upgrade).ConfigureAwait(true))
            {
                _recentBooks.Remember(SelectedPath);
                Unlocked?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (BookTooNewException tooNew)
        {
            // Refused rather than opened read-only, so there is nothing to offer beyond
            // saying which version can read it. The book has not been touched.
            ErrorMessage =
                $"This book was saved by a newer version of MyFinance (format {tooNew.BookVersion}; "
                + $"this version reads up to {tooNew.SupportedVersion}). Open it with that newer "
                + "version. Nothing has been changed.";
        }
        catch (IncorrectPasswordException)
        {
            ErrorMessage = "That password did not open this book.";
        }
        catch (BookNotFoundException)
        {
            ErrorMessage = "This book is missing its .mfmeta file, so it cannot be opened.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open the book: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Asks before updating a book written by an earlier version, then updates it.
    /// </summary>
    /// <remarks>
    /// An upgrade rewrites the only copy of somebody's records, so it is never done silently
    /// — and the prompt says what will be protected first, because "we backed it up" is only
    /// reassuring if it is said before rather than after.
    /// </remarks>
    private async Task<bool> TryUpgradeAsync(string path, string password, BookUpgradeRequiredException upgrade)
    {
        bool proceed = _dialogs.Confirm(
            "Update this book?",
            $"This book was saved by an earlier version of MyFinance (format {upgrade.BookVersion}; "
            + $"this version uses {upgrade.TargetVersion}).\n\n"
            + "It will be backed up before anything is changed, and the update is applied to a "
            + "copy — so if anything goes wrong your book is left exactly as it is now.\n\n"
            + "Update it now?");

        if (!proceed)
        {
            ErrorMessage = "This book needs updating before it can be opened.";
            return false;
        }

        try
        {
            string backup = await Task.Run(
                () => BookFileService.Upgrade(path, password)).ConfigureAwait(true);

            await Task.Run(() => _session.Open(path, password)).ConfigureAwait(true);

            if (!string.IsNullOrEmpty(backup))
            {
                _dialogs.ShowInformation(
                    "Book updated",
                    $"Your book has been updated, and the version from before was saved to:\n\n{backup}");
            }

            return true;
        }
        catch (BookUpgradeException failed)
        {
            // The only useful thing to say after a failed upgrade is which file to go back
            // to. An exception message without a remedy is no use to somebody whose book
            // will not open.
            ErrorMessage = failed.BackupPath is null
                ? failed.Message
                : $"{failed.Message} Nothing was changed, so you can try again — and if you need "
                  + $"to go back, that backup is at {failed.BackupPath}.";

            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"The book could not be updated: {ex.Message}";
            return false;
        }
    }

    // -- Create -------------------------------------------------------------------------

    [RelayCommand]
    private void BrowseForNewLocation()
    {
        string? path = _dialogs.PickNewBookLocation();
        if (path is not null)
        {
            SelectedPath = path;
        }
    }

    [RelayCommand]
    private async Task CreateAsync(PasswordPair? passwords)
    {
        if (passwords is null)
        {
            return;
        }

        if (SelectedPath is null)
        {
            ErrorMessage = "Choose where to save the book first.";
            return;
        }

        if (PasswordStrength.Evaluate(passwords.Password) == PasswordStrengthLevel.TooShort)
        {
            ErrorMessage = $"Use a password of at least {PasswordStrength.MinimumLength} characters.";
            return;
        }

        if (!string.Equals(passwords.Password, passwords.Confirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "The two passwords do not match.";
            return;
        }

        // There is genuinely no recovery path, so this is confirmed rather than assumed.
        bool confirmed = _dialogs.Confirm(
            "No password recovery",
            "Your book is encrypted with this password.\n\n"
            + "If you forget it, the data cannot be recovered — not by this application, "
            + "and not by anyone else.\n\nCreate the book?");

        if (!confirmed)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;

        try
        {
            string path = SelectedPath;
            string password = passwords.Password;

            await Task.Run(() => _session.Create(path, password)).ConfigureAwait(true);

            _recentBooks.Remember(path);

            // As on the unlock path: a session that began by creating a book is still a
            // session whose failures need telling apart from another book's.
            _diagnostics?.BookOpened(path, BookSchema.Current);
            _diagnostics?.Breadcrumb(Operation.CreateBook);

            Unlocked?.Invoke(this, EventArgs.Empty);
        }
        catch (BookFileException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not create the book: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Password and its confirmation, passed together from the create form.</summary>
public sealed record PasswordPair(string Password, string Confirmation);
