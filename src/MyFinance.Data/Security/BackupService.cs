using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Progress;

namespace MyFinance.Data.Security;

/// <summary>What a backup file says about itself.</summary>
/// <param name="Version">Format version of the archive.</param>
/// <param name="BookName">The book's file name when the backup was taken.</param>
/// <param name="CreatedUtc">When it was taken.</param>
/// <param name="Application">Which build wrote it.</param>
public sealed record BackupManifest(
    int Version,
    string BookName,
    DateTimeOffset CreatedUtc,
    string Application);

/// <summary>One backup file on disk.</summary>
public sealed record BackupEntry(string Path, BackupManifest Manifest, long SizeBytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public DateTimeOffset CreatedUtc => Manifest.CreatedUtc;

    public string CreatedText =>
        Manifest.CreatedUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);

    public string SizeText => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} bytes",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:N0} KB",
        _ => $"{SizeBytes / (1024.0 * 1024.0):N1} MB",
    };
}

/// <summary>Raised when a backup cannot be written, read or restored.</summary>
public sealed class BackupException : Exception
{
    public BackupException(string message)
        : base(message)
    {
    }

    public BackupException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Writes and restores backups of an encrypted book.
/// </summary>
/// <remarks>
/// <para>
/// A book is two files — the SQLCipher database and the plaintext sidecar carrying its salt
/// and cost parameters — and <b>both</b> are needed to open it. Losing the sidecar is exactly
/// as fatal as forgetting the password, so a backup that copies only the database is not a
/// backup at all. That is the entire reason this writes one archive holding the pair rather
/// than leaving people to copy files themselves.
/// </para>
/// <para>
/// The archive is not encrypted, and does not need to be: the database inside it is still
/// SQLCipher-encrypted under the same password, and the sidecar holds only a salt, which is
/// public by design. A backup is therefore exactly as safe to keep on a USB stick or in cloud
/// storage as the book itself.
/// </para>
/// <para>
/// There is still no password recovery. A backup restores the book as it was, password and
/// all; it does not and cannot help somebody who has forgotten it.
/// </para>
/// </remarks>
public static class BackupService
{
    public const string BackupExtension = ".mfbak";

    /// <summary>Current archive format.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Fixed names inside the archive.
    /// </summary>
    /// <remarks>
    /// Fixed rather than taken from the original file names, so restoring never has to treat
    /// a name inside the archive as a path. An archive that named its entry
    /// <c>..\..\Windows\System32\something</c> would otherwise write there.
    /// </remarks>
    private const string DatabaseEntry = "book.mfdb";
    private const string MetadataEntry = "book.mfmeta";
    private const string ManifestEntry = "backup.json";

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>The folder backups go in by default: beside the book, in "Backups".</summary>
    public static string DefaultDirectoryFor(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string? parent = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        return Path.Combine(parent ?? ".", "Backups");
    }

    /// <summary>
    /// A file name that sorts chronologically and says what it is at a glance.
    /// </summary>
    /// <remarks>
    /// Sortable-date first so a directory listing is in order without anybody having to
    /// think about it, and so the oldest is obvious when disk space runs short.
    /// </remarks>
    public static string SuggestFileName(string bookName, DateTimeOffset when) =>
        $"{bookName} {when.ToLocalTime():yyyy-MM-dd HHmm}{BackupExtension}";

    /// <summary>
    /// Backs up a book that is currently open.
    /// </summary>
    /// <remarks>
    /// The write-ahead log is folded back into the database first. Without that step the
    /// copy would be missing every change made since the last checkpoint — which is to say,
    /// most of the work the user did in this session, and precisely the work they are trying
    /// to protect.
    /// </remarks>
    public static BackupEntry Create(
        Book book,
        string destinationPath,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        progress?.Report(WorkProgress.Starting("Settling the book"));
        Checkpoint(book);

        return Create(book.DatabasePath, destinationPath, progress, cancellationToken);
    }

    /// <summary>Backs up a book by path, which must not be open for writing elsewhere.</summary>
    public static BackupEntry Create(
        string databasePath,
        string destinationPath,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string metadataPath = BookFileService.GetMetadataPath(databasePath);

        if (!File.Exists(databasePath))
        {
            throw new BackupException($"There is no book at {databasePath}.");
        }

        if (!File.Exists(metadataPath))
        {
            throw new BackupException(
                $"The book's key file is missing: {Path.GetFileName(metadataPath)}. "
                + "Without it the book cannot be opened even with the right password, so there is nothing worth backing up.");
        }

        var manifest = new BackupManifest(
            CurrentVersion,
            Path.GetFileNameWithoutExtension(databasePath),
            DateTimeOffset.UtcNow,
            typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "unknown");

        string? directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written beside the target and moved into place, so an interrupted backup cannot
        // leave a half-written file sitting where a good one used to be.
        string temporary = destinationPath + ".partial";

        try
        {
            progress?.Report(WorkProgress.Starting("Copying the book"));
            WriteArchive(temporary, databasePath, metadataPath, manifest, progress, cancellationToken);

            progress?.Report(WorkProgress.Starting("Checking the backup can be read"));
            Verify(temporary);

            File.Move(temporary, destinationPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            // A cancelled backup leaves nothing behind, so the previous good one still stands.
            TryDelete(temporary);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(temporary);
            throw new BackupException($"The backup could not be written: {ex.Message}", ex);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        return new BackupEntry(destinationPath, manifest, new FileInfo(destinationPath).Length);
    }

    private static void WriteArchive(
        string archivePath,
        string databasePath,
        string metadataPath,
        BackupManifest manifest,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        AddFile(archive, databasePath, DatabaseEntry, progress, cancellationToken);
        AddFile(archive, metadataPath, MetadataEntry, progress, cancellationToken);

        ZipArchiveEntry entry = archive.CreateEntry(ManifestEntry, CompressionLevel.NoCompression);
        using Stream target = entry.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(manifest, ManifestOptions));
    }

    private static void AddFile(
        ZipArchive archive,
        string path,
        string entryName,
        IProgress<WorkProgress>? progress,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

        // Shared read: the book may well be open in this very process.
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using Stream target = entry.Open();

        // Copied in chunks rather than in one call, so a large book can report how far it has
        // got and can be stopped part-way.
        const int ChunkSize = 256 * 1024;
        var buffer = new byte[ChunkSize];

        int total = (int)Math.Min(source.Length / 1024, int.MaxValue);
        int copied = 0;
        int read;

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            target.Write(buffer, 0, read);
            copied += read / 1024;

            progress?.Report(new WorkProgress("Copying the book", Math.Min(copied, total), total));
        }
    }

    /// <summary>
    /// Reads the archive back before it is accepted.
    /// </summary>
    /// <remarks>
    /// A backup nobody has read is a guess. This costs a second and catches a truncated
    /// write, a full disk or a failing drive at the only moment anything can still be done
    /// about it — rather than years later, when it is the only copy left.
    /// </remarks>
    private static void Verify(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        if (archive.GetEntry(DatabaseEntry) is not ZipArchiveEntry database || database.Length == 0)
        {
            throw new BackupException("The backup was written but does not contain a readable book.");
        }

        if (archive.GetEntry(MetadataEntry) is not ZipArchiveEntry metadata || metadata.Length == 0)
        {
            throw new BackupException("The backup was written but does not contain the book's key file.");
        }

        // Read every byte back, so a corrupted compressed stream is found now and not later.
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using Stream stream = entry.Open();
            stream.CopyTo(Stream.Null);
        }
    }

    /// <summary>Reads what a backup says about itself, without restoring it.</summary>
    public static BackupEntry Inspect(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);

            if (archive.GetEntry(DatabaseEntry) is null || archive.GetEntry(MetadataEntry) is null)
            {
                throw new BackupException(
                    $"{Path.GetFileName(archivePath)} is not a {BackupExtension} backup of a book.");
            }

            BackupManifest manifest = ReadManifest(archive, archivePath);
            return new BackupEntry(archivePath, manifest, new FileInfo(archivePath).Length);
        }
        catch (InvalidDataException ex)
        {
            throw new BackupException(
                $"{Path.GetFileName(archivePath)} is damaged and cannot be read: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new BackupException($"{Path.GetFileName(archivePath)} could not be read: {ex.Message}", ex);
        }
    }

    private static BackupManifest ReadManifest(ZipArchive archive, string archivePath)
    {
        if (archive.GetEntry(ManifestEntry) is not ZipArchiveEntry entry)
        {
            // An archive with the two files but no manifest is still perfectly restorable.
            return new BackupManifest(
                CurrentVersion,
                Path.GetFileNameWithoutExtension(archivePath),
                File.GetLastWriteTimeUtc(archivePath),
                "unknown");
        }

        try
        {
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream);

            return JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd())
                ?? throw new BackupException("The backup's description could not be read.");
        }
        catch (JsonException)
        {
            return new BackupManifest(
                CurrentVersion,
                Path.GetFileNameWithoutExtension(archivePath),
                File.GetLastWriteTimeUtc(archivePath),
                "unknown");
        }
    }

    /// <summary>Lists the backups in a folder, newest first.</summary>
    public static IReadOnlyList<BackupEntry> List(string directory, string? bookName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var found = new List<BackupEntry>();

        foreach (string path in Directory.EnumerateFiles(directory, $"*{BackupExtension}"))
        {
            BackupEntry entry;

            try
            {
                entry = Inspect(path);
            }
            catch (BackupException)
            {
                // A damaged file in the folder must not hide the good ones beside it.
                continue;
            }

            if (bookName is null
                || string.Equals(entry.Manifest.BookName, bookName, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(entry);
            }
        }

        return [.. found.OrderByDescending(e => e.CreatedUtc).ThenBy(e => e.FileName, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Deletes all but the newest few backups of one book.
    /// </summary>
    /// <remarks>
    /// Deliberately conservative: it only ever considers files that inspect cleanly as
    /// backups of this particular book, and refuses to delete anything at all when asked to
    /// keep fewer than one. Automatic deletion of the wrong file is worse than a full disk.
    /// </remarks>
    public static int Prune(string directory, string bookName, int keep)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookName);

        if (keep < 1)
        {
            return 0;
        }

        IReadOnlyList<BackupEntry> existing = List(directory, bookName);
        int removed = 0;

        foreach (BackupEntry entry in existing.Skip(keep))
        {
            if (TryDelete(entry.Path))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Restores a backup to a book path.
    /// </summary>
    /// <remarks>
    /// Both halves are written or neither is. A restore that laid down the database and then
    /// failed on the sidecar would leave a book that cannot be opened by anybody, with the
    /// original already overwritten.
    /// </remarks>
    public static void Restore(string archivePath, string destinationDatabasePath, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDatabasePath);

        string destinationMetadata = BookFileService.GetMetadataPath(destinationDatabasePath);

        if (!overwrite && (File.Exists(destinationDatabasePath) || File.Exists(destinationMetadata)))
        {
            throw new BackupException(
                $"There is already a book at {Path.GetFileName(destinationDatabasePath)}. "
                + "Restore to a new name, or say explicitly that it should be replaced.");
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(destinationDatabasePath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryDatabase = destinationDatabasePath + ".restoring";
        string temporaryMetadata = destinationMetadata + ".restoring";

        try
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                // By fixed name, never by whatever the archive calls its entries.
                Extract(archive, DatabaseEntry, temporaryDatabase, archivePath);
                Extract(archive, MetadataEntry, temporaryMetadata, archivePath);
            }

            // The sidecar is tiny and lands first; if the disk is full it fails here, before
            // the database it belongs to has replaced anything.
            File.Move(temporaryMetadata, destinationMetadata, overwrite: true);
            File.Move(temporaryDatabase, destinationDatabasePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(temporaryDatabase);
            TryDelete(temporaryMetadata);
            throw new BackupException($"The backup could not be restored: {ex.Message}", ex);
        }
        catch
        {
            TryDelete(temporaryDatabase);
            TryDelete(temporaryMetadata);
            throw;
        }

        // Stale journal files from whatever used to be at this path would be read as part of
        // the restored database and would corrupt it.
        TryDelete(destinationDatabasePath + "-wal");
        TryDelete(destinationDatabasePath + "-shm");
    }

    private static void Extract(ZipArchive archive, string entryName, string path, string archivePath)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new BackupException(
                $"{Path.GetFileName(archivePath)} is missing {entryName} and cannot be restored.");

        using Stream source = entry.Open();
        using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    /// <summary>Folds the write-ahead log back into the database file.</summary>
    private static void Checkpoint(Book book)
    {
        try
        {
            using MyFinanceDbContext db = book.CreateContext();
            db.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch (SqliteException ex)
        {
            throw new BackupException(
                $"The book could not be brought to a consistent state to back up: {ex.Message}", ex);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (IOException)
        {
            // A backup left behind is untidy; failing the whole operation over it is worse.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }
}
