namespace MyFinance.App.Services;

/// <summary>
/// File pickers and message boxes, behind an interface so view models stay testable and
/// free of direct WPF dependencies.
/// </summary>
public interface IDialogService
{
    /// <summary>Prompts for an existing book file. Returns null if cancelled.</summary>
    string? PickBookToOpen();

    /// <summary>Prompts for a location for a new book file. Returns null if cancelled.</summary>
    string? PickNewBookLocation();

    /// <summary>Prompts for a downloaded bank statement. Returns null if cancelled.</summary>
    string? PickStatementFile();

    /// <summary>Asks for a Microsoft Money file to bring across.</summary>
    string? PickMoneyFile();

    /// <summary>Asks for a backup file to restore.</summary>
    string? PickBackupToRestore();

    /// <summary>Asks where to write a backup, suggesting a name.</summary>
    string? PickBackupDestination(string suggestedFileName);

    /// <summary>Asks for a folder to keep backups in.</summary>
    string? PickBackupFolder(string? current);

    /// <summary>Prompts for where to write an export. Returns null if cancelled.</summary>
    string? PickExportLocation(string suggestedFileName);

    /// <summary>Shows a folder in the file manager.</summary>
    void OpenFolder(string path);

    void ShowError(string title, string message);

    void ShowInformation(string title, string message);

    bool Confirm(string title, string message);
}
