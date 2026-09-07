using System.Globalization;
using System.Text;

namespace MyFinance.Core.Diagnostics;

/// <summary>How much a log level says.</summary>
public enum LogLevel
{
    /// <summary>Which operation ran. Written only when the user turns verbose on.</summary>
    Breadcrumb = 0,

    /// <summary>Something failed.</summary>
    Failure = 1,
}

/// <summary>
/// One line of the log.
/// </summary>
/// <remarks>
/// Everything here is either a fixed vocabulary (the operation, the level), a number, or comes
/// from an exception's type and stack. The one field that could carry the user's own words —
/// <see cref="Message"/> — has already been through <see cref="SafeMessages"/> before it gets
/// this far.
/// </remarks>
public sealed record LogEntry
{
    public required DateTimeOffset At { get; init; }

    public required LogLevel Level { get; init; }

    public required Operation Operation { get; init; }

    /// <summary>Full type name, always recorded — it is what locates a fault.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Scrubbed, or <see cref="SafeMessages.Withheld"/>.</summary>
    public string? Message { get; init; }

    /// <summary>Stack trace, with file paths removed.</summary>
    public string? Stack { get; init; }

    /// <summary>Inner exception types and messages, outermost first.</summary>
    public IReadOnlyList<string> Inner { get; init; } = [];

    /// <summary>The first question about any fault report.</summary>
    public required string AppVersion { get; init; }

    /// <summary>Which book, without saying which book. See <see cref="BookTag"/>.</summary>
    public string Book { get; init; } = BookTag.None;

    /// <summary>The open book's schema version, or null when none is open.</summary>
    public int? SchemaVersion { get; init; }

    /// <summary>How many times this same failure happened. One unless it repeated.</summary>
    public int Count { get; init; } = 1;

    /// <summary>
    /// Builds an entry from an exception, taking only what is safe to keep.
    /// </summary>
    public static LogEntry FromException(
        Operation operation,
        Exception exception,
        string appVersion,
        string book,
        int? schemaVersion,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var inner = new List<string>();

        for (Exception? e = exception.InnerException; e is not null; e = e.InnerException)
        {
            inner.Add($"{e.GetType().FullName}: {SafeMessages.For(e)}");
        }

        return new LogEntry
        {
            At = (clock ?? TimeProvider.System).GetUtcNow(),
            Level = LogLevel.Failure,
            Operation = operation,
            ExceptionType = exception.GetType().FullName,
            Message = SafeMessages.For(exception),

            // The stack names source files, and those paths contain whoever built it. Scrubbed
            // like everything else; the type and method names are what matter.
            Stack = PathScrubber.Scrub(exception.StackTrace),
            Inner = inner,
            AppVersion = appVersion,
            Book = book,
            SchemaVersion = schemaVersion,
        };
    }

    /// <summary>
    /// The entry as it appears in the file.
    /// </summary>
    /// <remarks>
    /// Invariant culture throughout: a log read on a machine set to another locale must say the
    /// same thing, and a timestamp that changes format with the reader is one nobody can sort.
    /// </remarks>
    public string Render()
    {
        var text = new StringBuilder();

        text.Append(At.ToString("yyyy-MM-dd HH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        text.Append("  ").Append(Level == LogLevel.Failure ? "FAIL" : "----");
        text.Append("  ").Append(Operation);
        text.Append("  [v").Append(AppVersion);
        text.Append(" book:").Append(Book);

        if (SchemaVersion is int schema)
        {
            text.Append(" schema:").Append(schema.ToString(CultureInfo.InvariantCulture));
        }

        text.Append(']');

        if (Count > 1)
        {
            text.Append("  (×").Append(Count.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        if (ExceptionType is not null)
        {
            text.AppendLine();
            text.Append("    ").Append(ExceptionType).Append(": ").Append(Message);

            foreach (string inner in Inner)
            {
                text.AppendLine();
                text.Append("     ---> ").Append(inner);
            }

            if (!string.IsNullOrWhiteSpace(Stack))
            {
                text.AppendLine();
                text.Append(Stack);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// What makes two failures "the same" for the purpose of collapsing repeats.
    /// </summary>
    /// <remarks>
    /// Operation, type and stack — not the timestamp or the count. A fault in a filter handler
    /// fires on every keystroke, and without this the log becomes a thousand copies of one fact
    /// with the entry that explains it already rolled off the end.
    /// </remarks>
    public string Signature => $"{Operation}|{ExceptionType}|{Stack}";
}
