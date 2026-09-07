using System.Windows;
using Microsoft.Win32;
using MyFinance.Data.Security;

namespace MyFinance.App.Services;

/// <inheritdoc cref="IDialogService" />
public sealed class DialogService : IDialogService
{
    private readonly DiagnosticSink? _diagnostics;

    public DialogService(DiagnosticSink? diagnostics = null) => _diagnostics = diagnostics;

    private const string BookFilter = "MyFinance book (*.mfdb)|*.mfdb|All files (*.*)|*.*";

    // QFX and QBO are OFX with vendor extensions and go through the same reader; QIF is a
    // different format entirely, and which one a file actually is comes from its contents
    // rather than its extension.
    private const string StatementFilter =
        "Bank statements (*.ofx;*.qfx;*.qbo;*.qif)|*.ofx;*.qfx;*.qbo;*.qif"
        + "|OFX (*.ofx;*.qfx;*.qbo)|*.ofx;*.qfx;*.qbo"
        + "|Quicken interchange (*.qif)|*.qif"
        + "|All files (*.*)|*.*";

    public string? PickBookToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open book",
            Filter = BookFilter,
            DefaultExt = BookFileService.DatabaseExtension,
            CheckFileExists = true,
            InitialDirectory = DefaultBookDirectory(),
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickNewBookLocation()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create book",
            Filter = BookFilter,
            DefaultExt = BookFileService.DatabaseExtension,
            AddExtension = true,
            FileName = "My Finances.mfdb",
            OverwritePrompt = false, // Create refuses to overwrite; a prompt here would mislead.
            InitialDirectory = DefaultBookDirectory(),
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickStatementFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a statement",
            Filter = StatementFilter,
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is string home
                && Directory.Exists(Path.Combine(home, "Downloads"))
                    ? Path.Combine(home, "Downloads")
                    : DefaultBookDirectory(),
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Asks for a Microsoft Money file.
    /// </summary>
    /// <remarks>
    /// Money's own backups end in <c>.mbf</c> and are offered too, since a backup is often
    /// the only copy somebody can still lay hands on. Whether one actually reads is decided
    /// by the reader when it opens the file, not by the extension.
    /// </remarks>
    public string? PickMoneyFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Bring across a Microsoft Money file",
            Filter = "Microsoft Money files (*.mny;*.mbf)|*.mny;*.mbf|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) is string documents
                && Directory.Exists(documents)
                    ? documents
                    : DefaultBookDirectory(),
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private const string BackupFilter = "Book backups (*.mfbak)|*.mfbak|All files (*.*)|*.*";

    public string? PickBackupToRestore()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore from a backup",
            Filter = BackupFilter,
            CheckFileExists = true,
            InitialDirectory = DefaultBookDirectory(),
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickBackupDestination(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Back up to",
            Filter = BackupFilter,
            DefaultExt = BackupService.BackupExtension,
            FileName = suggestedFileName,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = DefaultBookDirectory(),
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Asks for a folder using the save dialog's own folder mode.
    /// </summary>
    /// <remarks>
    /// WPF has no folder picker of its own, and reaching for the WinForms one drags a second
    /// UI framework into the process for a single dialog.
    /// </remarks>
    public string? PickBackupFolder(string? current)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Where should backups be kept?",
            InitialDirectory = current is not null && Directory.Exists(current)
                ? current
                : DefaultBookDirectory(),
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>
    /// Asks where to write an export.
    /// </summary>
    /// <remarks>
    /// The escape hatch this application exists to provide: nothing in a book should be
    /// readable only from inside this program, which is the position Money left the user in.
    /// </remarks>
    public string? PickExportLocation(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export",
            Filter = "Comma-separated values (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = suggestedFileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Opens a folder in Explorer.
    /// </summary>
    /// <remarks>
    /// How somebody actually gets a backup onto a memory stick or into cloud storage. The
    /// application will not copy it there for them, so it at least shows them where it is.
    /// </remarks>
    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ShowInformation("Backups", "That folder does not exist yet. Take a backup and it will.");
            return;
        }

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ShowError("Backups", $"That folder could not be opened: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows an error, and records that one was shown.
    /// </summary>
    /// <remarks>
    /// The <paramref name="message"/> is deliberately <b>not</b> logged. It is free text built
    /// from an exception, and in this application that routinely means a payee or an amount.
    /// What goes to the log is the timestamp — enough to line a user's "it broke this morning"
    /// up with the breadcrumb saying what was running, and nothing more.
    /// </remarks>
    public void ShowError(string title, string message)
    {
        _diagnostics?.UserFacingError();
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowInformation(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    /// <summary>Books default to Documents\MyFinance, created on first use.</summary>
    internal static string DefaultBookDirectory()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MyFinance");

        Directory.CreateDirectory(directory);
        return directory;
    }
}
