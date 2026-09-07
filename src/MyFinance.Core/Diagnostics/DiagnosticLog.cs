using System.Collections.Concurrent;
using System.Text;

namespace MyFinance.Core.Diagnostics;

/// <summary>
/// Where the log is kept and how much of it.
/// </summary>
/// <param name="Directory">
/// The folder. In the application this is under the user's local application data, never
/// beside the book: a book frequently sits in a synced folder, and a log written there would
/// be copied off the machine as a side effect of where the book happens to live.
/// </param>
/// <param name="MaximumBytes">Roll over past this. Two files are kept.</param>
/// <param name="Verbose">Whether breadcrumbs are recorded as well as failures.</param>
public sealed record DiagnosticOptions(
    string Directory,
    long MaximumBytes = 1024 * 1024,
    bool Verbose = false)
{
    public const string FileName = "myfinance.log";

    public const string PreviousFileName = "myfinance.1.log";

    public string Path => System.IO.Path.Combine(Directory, FileName);

    public string PreviousPath => System.IO.Path.Combine(Directory, PreviousFileName);
}

/// <summary>
/// Writes what went wrong, and nothing about what the book contains.
/// </summary>
/// <remarks>
/// <para>
/// The API takes an <see cref="Operation"/> — an enum — and an <see cref="Exception"/>. There
/// is deliberately no parameter that could carry a payee, an amount or a path, so redaction is
/// something the type system does rather than something 59 <c>catch</c> blocks remember.
/// </para>
/// <para>
/// Nothing here may become the failure it exists to record. Entries go on a bounded queue and
/// a background thread drains it; a full queue drops its oldest entry rather than making the
/// application wait, and every write is wrapped so that a read-only folder, a full disk or a
/// locked file leaves the application exactly as it was.
/// </para>
/// <para>
/// Each entry is flushed as it is written. That costs throughput and buys the thing that
/// matters: the last entry before a crash is the one worth having, and it is on disk.
/// </para>
/// </remarks>
public sealed class DiagnosticLog : IDisposable
{
    /// <summary>Beyond this many queued entries, the oldest is dropped.</summary>
    public const int QueueLimit = 512;

    private readonly DiagnosticOptions _options;
    private readonly TimeProvider _clock;
    private readonly BlockingCollection<LogEntry> _queue;
    private readonly Thread? _writer;
    private readonly Lock _file = new();
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    private string _appVersion = "unknown";
    private string _book = BookTag.None;
    private int? _schemaVersion;
    private bool _disposed;

    public DiagnosticLog(DiagnosticOptions options, TimeProvider? clock = null, bool startWriter = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _clock = clock ?? TimeProvider.System;
        _queue = new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>(), QueueLimit);

        Verbose = options.Verbose;

        if (startWriter)
        {
            _writer = new Thread(Drain)
            {
                IsBackground = true,
                Name = "MyFinance diagnostics",
            };

            _writer.Start();
        }
    }

    public DiagnosticOptions Options => _options;

    /// <summary>
    /// Whether breadcrumbs are recorded. Switchable while running, so turning it on to
    /// reproduce a fault does not mean restarting and losing the state that provokes it.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>How many entries were dropped because the queue was full.</summary>
    public int Dropped { get; private set; }

    /// <summary>Records the build, so a fault report answers "which version" without asking.</summary>
    public void Describe(string appVersion) => _appVersion = appVersion;

    /// <summary>
    /// Records which book is open — as a tag, never a name or a path.
    /// </summary>
    public void BookOpened(string? databasePath, int? schemaVersion)
    {
        _book = BookTag.For(databasePath);
        _schemaVersion = schemaVersion;
    }

    public void BookClosed()
    {
        _book = BookTag.None;
        _schemaVersion = null;
    }

    /// <summary>Records a failure.</summary>
    public void Failure(Operation operation, Exception exception)
    {
        if (exception is null)
        {
            return;
        }

        Enqueue(LogEntry.FromException(
            operation, exception, _appVersion, _book, _schemaVersion, _clock));
    }

    /// <summary>
    /// Records that an operation ran. Silent unless the user has turned verbose on.
    /// </summary>
    /// <remarks>
    /// Breadcrumbs are what make an intermittent fault findable: the exception says what broke,
    /// and the trail says what led up to it. They record <em>which</em> operations ran and in
    /// what order — never their arguments.
    /// </remarks>
    public void Breadcrumb(Operation operation)
    {
        if (!Verbose)
        {
            return;
        }

        Enqueue(new LogEntry
        {
            At = _clock.GetUtcNow(),
            Level = LogLevel.Breadcrumb,
            Operation = operation,
            AppVersion = _appVersion,
            Book = _book,
            SchemaVersion = _schemaVersion,
        });
    }

    private void Enqueue(LogEntry entry)
    {
        if (_disposed)
        {
            return;
        }

        // Never blocks. A log that made the application wait to record that the application is
        // unwell would be worse than a gap in the log.
        if (!_queue.TryAdd(entry))
        {
            Dropped++;
        }
    }

    private void Drain()
    {
        foreach (LogEntry entry in _queue.GetConsumingEnumerable())
        {
            Write(entry);
        }
    }

    /// <summary>Drains the queue on this thread. For tests, and for shutdown.</summary>
    public void Flush()
    {
        while (_queue.TryTake(out LogEntry? entry))
        {
            Write(entry);
        }
    }

    private void Write(LogEntry entry)
    {
        lock (_file)
        {
            // A repeat of something already recorded is counted, not written again.
            if (entry.Level == LogLevel.Failure)
            {
                if (_seen.TryGetValue(entry.Signature, out int count))
                {
                    _seen[entry.Signature] = count + 1;

                    // Powers of two, so a fault firing on every keystroke leaves a handful of
                    // lines saying it is still happening rather than thousands saying nothing
                    // new.
                    if (!IsPowerOfTwo(count + 1))
                    {
                        return;
                    }

                    entry = entry with { Count = count + 1 };
                }
                else
                {
                    _seen[entry.Signature] = 1;
                }
            }

            Append(entry.Render());
        }
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    /// <summary>
    /// Appends one line, rolling the file over first if it has grown past the cap.
    /// </summary>
    /// <remarks>
    /// Every failure here is swallowed. This must never be the reason an operation fails or a
    /// message reaches the user — the point of the log is to explain problems, not to add one.
    /// </remarks>
    private void Append(string line)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_options.Directory);
            RollIfNeeded();

            // Shared so two copies of the single-file executable running at once interleave
            // rather than one of them failing outright.
            using var stream = new FileStream(
                _options.Path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);

            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.WriteLine(line);

            // Flushed per entry: the last one before a crash is the one worth having.
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (IsWriteFailure(ex))
        {
        }
    }

    /// <summary>
    /// Everything that can go wrong reaching a file, and all of it survivable.
    /// </summary>
    /// <remarks>
    /// Deliberately broad — this is the one place in the solution where swallowing widely is
    /// right. A read-only folder throws <see cref="UnauthorizedAccessException"/>, a full disk
    /// or a disconnected drive an <see cref="IOException"/>, and a path the configuration got
    /// wrong an <see cref="ArgumentException"/> from <c>CreateDirectory</c> before any of the
    /// rest is reached. None of them may become the reason an operation failed.
    /// </remarks>
    private static bool IsWriteFailure(Exception exception) => exception is
        IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or ArgumentException
        or System.Security.SecurityException;

    private void RollIfNeeded()
    {
        var current = new FileInfo(_options.Path);

        if (!current.Exists || current.Length < _options.MaximumBytes)
        {
            return;
        }

        try
        {
            // One rollover. The previous file is replaced, so two files is the ceiling however
            // long the application runs.
            File.Move(_options.Path, _options.PreviousPath, overwrite: true);
        }
        catch (Exception ex) when (IsWriteFailure(ex))
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();

        // A short wait: shutdown must not hang on a disk that is not answering.
        _writer?.Join(TimeSpan.FromSeconds(2));

        Flush();
        _queue.Dispose();
    }
}
