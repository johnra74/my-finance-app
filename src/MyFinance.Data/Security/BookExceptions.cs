namespace MyFinance.Data.Security;

/// <summary>Base for problems opening or creating a book file.</summary>
public class BookFileException : Exception
{
    public BookFileException(string message)
        : base(message)
    {
    }

    public BookFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The supplied password does not decrypt the book.
/// </summary>
/// <remarks>
/// Deliberately indistinguishable from any other wrong-password outcome: the message never
/// hints at how close a guess was, and the same exception is raised whether the failure
/// surfaced at the key pragma or at the first page read.
/// </remarks>
public sealed class IncorrectPasswordException : BookFileException
{
    public IncorrectPasswordException()
        : base("The password is incorrect.")
    {
    }
}

/// <summary>The book file or its key parameter sidecar is missing.</summary>
public sealed class BookNotFoundException : BookFileException
{
    public BookNotFoundException(string path)
        : base($"No book was found at '{path}'.")
    {
        Path = path;
    }

    public string Path { get; }
}

/// <summary>
/// The book was written by a newer build than this one.
/// </summary>
/// <remarks>
/// Refused rather than opened read-only. A read-only mode would have to be honoured by every
/// write path in the application, and the first path that forgot would write a row shaped for
/// a schema it does not understand — into a file with no recovery. This follows the precedent
/// <see cref="BookKeyParameters.FromJson"/> already sets for the sidecar: refuse what you do
/// not understand rather than reading it optimistically.
/// </remarks>
public sealed class BookTooNewException : BookFileException
{
    public BookTooNewException(int bookVersion, int supportedVersion)
        : base($"This book was saved by a newer version of MyFinance (book format {bookVersion}, "
             + $"this version reads up to {supportedVersion}). Open it with that newer version. "
             + "The book has not been changed.")
    {
        BookVersion = bookVersion;
        SupportedVersion = supportedVersion;
    }

    public int BookVersion { get; }

    public int SupportedVersion { get; }
}

/// <summary>
/// The book was written by an older build and must be upgraded before it can be opened.
/// </summary>
/// <remarks>
/// Raised rather than upgrading silently, because an upgrade rewrites the only copy of the
/// user's records and they are entitled to be told first — and to be told what was protected
/// before it started. <see cref="BookFileService.Upgrade"/> is the deliberate second step.
/// </remarks>
public sealed class BookUpgradeRequiredException : BookFileException
{
    public BookUpgradeRequiredException(int bookVersion, int targetVersion)
        : base($"This book was saved by an earlier version of MyFinance (book format {bookVersion}, "
             + $"this version uses {targetVersion}). It needs to be updated before it can be opened.")
    {
        BookVersion = bookVersion;
        TargetVersion = targetVersion;
    }

    public int BookVersion { get; }

    public int TargetVersion { get; }
}

/// <summary>
/// An upgrade could not be started, or did not finish.
/// </summary>
/// <remarks>
/// Carries the pre-upgrade backup, because the only useful thing to say after a failed
/// upgrade is which file to go back to. A message that reports an exception and not a remedy
/// is no use to somebody whose book will not open.
/// </remarks>
public sealed class BookUpgradeException : BookFileException
{
    public BookUpgradeException(string message, string? backupPath = null, Exception? innerException = null)
        : base(backupPath is null ? message : $"{message} Your book was backed up first, to {backupPath}.",
               innerException ?? new InvalidOperationException(message))
    {
        BackupPath = backupPath;
    }

    /// <summary>The verified backup taken before the upgrade began, when one was taken.</summary>
    public string? BackupPath { get; }
}
