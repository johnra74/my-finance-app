using MyFinance.Core.Diagnostics;

namespace MyFinance.App.Services;

/// <summary>
/// Hands the application's one log to whoever needs it.
/// </summary>
/// <remarks>
/// The log is created before the service provider exists — a failure during startup has to be
/// recordable too — so it is handed in afterwards rather than constructed by the container.
/// Everything here is null-safe: nothing may fail because the log is not ready.
/// </remarks>
public sealed class DiagnosticSink
{
    private DiagnosticLog? _log;

    public void Use(DiagnosticLog? log) => _log = log;

    public string Directory => DiagnosticLogFactory.Directory;

    public void Failure(Operation operation, Exception exception) =>
        _log?.Failure(operation, exception);

    public void Breadcrumb(Operation operation) => _log?.Breadcrumb(operation);

    public void BookOpened(string? databasePath, int? schemaVersion) =>
        _log?.BookOpened(databasePath, schemaVersion);

    public void BookClosed() => _log?.BookClosed();

    /// <summary>Whether breadcrumbs are being recorded.</summary>
    public bool Verbose
    {
        get => _log?.Verbose ?? false;
        set
        {
            if (_log is not null)
            {
                _log.Verbose = value;
            }
        }
    }

    /// <summary>
    /// Records that an error was shown to the user — the fact, not the words.
    /// </summary>
    /// <remarks>
    /// The message cannot be kept: <c>ShowError</c> is handed free text, and in this
    /// application that text routinely contains a payee or an amount lifted from an
    /// exception. What is worth keeping is the timestamp, which lets "it went wrong just
    /// after lunch" line up with the breadcrumb that says which operation was running.
    /// </remarks>
    public void UserFacingError() => _log?.Failure(
        Operation.Unknown,
        new UserFacingErrorMarker());
}

/// <summary>
/// Stands in for an error the user was shown, carrying nothing about it.
/// </summary>
/// <remarks>
/// A marker rather than the real exception, because by the time <c>ShowError</c> is reached
/// the exception has already been turned into a sentence and the sentence is the part that is
/// unsafe to keep. Where the underlying exception is still in hand, callers log it directly.
/// </remarks>
public sealed class UserFacingErrorMarker : Exception
{
    public UserFacingErrorMarker()
        : base("An error was shown to the user.")
    {
    }
}
