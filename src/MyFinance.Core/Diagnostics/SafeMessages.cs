namespace MyFinance.Core.Diagnostics;

/// <summary>
/// Decides whether an exception's message may be written down.
/// </summary>
/// <remarks>
/// <para>
/// The exception is the one place unbounded text arrives: a message can contain whatever the
/// thrower put in it, and in this solution some of them contain the user's data outright.
/// <c>BookValidationException</c> messages name payees — <em>"…has nothing outstanding to
/// enter"</em> is prefixed with the payee's name — and import diagnostics quote bank
/// descriptors.
/// </para>
/// <para>
/// So a message is quoted only when its type is one this application knows cannot carry user
/// data: the framework's own argument and IO exceptions, which say things like "Access to the
/// path is denied" and name a type or a parameter. Everything else records the type and says
/// the message was withheld.
/// </para>
/// <para>
/// Losing a message costs less than leaking one, and it costs less than it sounds: the
/// exception type and the stack trace are what actually locate a fault. A message is a
/// convenience.
/// </para>
/// </remarks>
public static class SafeMessages
{
    /// <summary>What is written in place of a message that was not quoted.</summary>
    public const string Withheld = "(message withheld — may contain your data)";

    /// <summary>
    /// Exception types whose messages are framework-generated and carry nothing of the user's.
    /// </summary>
    /// <remarks>
    /// An allow-list rather than a deny-list. A new exception type added anywhere in this
    /// solution is withheld by default, which is the direction the mistake should fall in.
    /// </remarks>
    private static readonly HashSet<string> SafeTypes = new(StringComparer.Ordinal)
    {
        "System.ArgumentException",
        "System.ArgumentNullException",
        "System.ArgumentOutOfRangeException",
        "System.InvalidOperationException",
        "System.NotSupportedException",
        "System.NotImplementedException",
        "System.NullReferenceException",
        "System.IndexOutOfRangeException",
        "System.OverflowException",
        "System.FormatException",
        "System.TimeoutException",
        "System.OperationCanceledException",
        "System.ObjectDisposedException",
        "System.OutOfMemoryException",
        "System.StackOverflowException",
        "System.InvalidCastException",
        "System.UnauthorizedAccessException",
        "System.IO.IOException",
        "System.IO.FileNotFoundException",
        "System.IO.DirectoryNotFoundException",
        "System.IO.PathTooLongException",
        "System.IO.InvalidDataException",
        "System.IO.EndOfStreamException",
        "System.Threading.Tasks.TaskCanceledException",
    };

    /// <summary>
    /// The message, or <see cref="Withheld"/>.
    /// </summary>
    /// <remarks>
    /// A path in the message is still redacted even for a safe type: <c>IOException</c> names
    /// the file it failed on, and that file is frequently the user's book.
    /// </remarks>
    public static string For(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string type = exception.GetType().FullName ?? exception.GetType().Name;

        return SafeTypes.Contains(type) ? PathScrubber.Scrub(exception.Message) : Withheld;
    }

    /// <summary>Whether this type's messages are quoted.</summary>
    public static bool IsSafe(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SafeTypes.Contains(type.FullName ?? type.Name);
    }
}

/// <summary>
/// Strips anything that looks like a file path out of a line of text.
/// </summary>
/// <remarks>
/// A path is the quiet leak in a diagnostics file. <c>C:\Users\Jane Smith\OneDrive\Divorce
/// settlement.mfdb</c> names the person, where they keep their records, and what the records
/// are about — from an exception message nobody thought of as sensitive.
/// </remarks>
public static class PathScrubber
{
    public const string Replacement = "<path>";

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = new System.Text.StringBuilder(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            int start = FindPathStart(text, i);

            if (start < 0)
            {
                result.Append(text, i, text.Length - i);
                break;
            }

            result.Append(text, i, start - i);
            result.Append(Replacement);

            i = start;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"' && text[i] != '\'')
            {
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>A Windows drive root, a UNC share, or a POSIX absolute path.</summary>
    private static int FindPathStart(string text, int from)
    {
        for (int i = from; i < text.Length; i++)
        {
            if (i + 2 < text.Length && char.IsLetter(text[i]) && text[i + 1] == ':'
                && (text[i + 2] == '\\' || text[i + 2] == '/'))
            {
                return i;
            }

            if (i + 1 < text.Length && text[i] == '\\' && text[i + 1] == '\\')
            {
                return i;
            }

            // A POSIX path, but only at a word boundary — otherwise every "and/or" matches.
            if (text[i] == '/' && (i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == '\''
                                   || text[i - 1] == '"')
                && i + 1 < text.Length && char.IsLetter(text[i + 1]))
            {
                return i;
            }
        }

        return -1;
    }
}
