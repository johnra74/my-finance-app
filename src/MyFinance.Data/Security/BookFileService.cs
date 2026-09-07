using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Progress;
using MyFinance.Data.Services;

namespace MyFinance.Data.Security;

/// <summary>
/// Creates, opens and re-keys encrypted book files.
/// </summary>
/// <remarks>
/// A book is two files: <c>name.mfdb</c>, the SQLCipher database, and <c>name.mfmeta</c>,
/// the plaintext sidecar holding the Argon2id salt and cost parameters. Both are required
/// to open the book — losing the sidecar is as fatal as losing the password, which is why
/// backups must copy the pair.
/// </remarks>
public static class BookFileService
{
    public const string DatabaseExtension = ".mfdb";

    public const string MetadataExtension = ".mfmeta";

    /// <summary>Path of the sidecar that accompanies the given database file.</summary>
    public static string GetMetadataPath(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return Path.ChangeExtension(databasePath, MetadataExtension);
    }

    /// <summary>True when both halves of a book are present at the given path.</summary>
    public static bool Exists(string databasePath) =>
        File.Exists(databasePath) && File.Exists(GetMetadataPath(databasePath));

    /// <summary>
    /// Creates a new encrypted book and applies the schema.
    /// </summary>
    /// <remarks>
    /// There is no password recovery. The key exists only as a function of the password and
    /// the salt, and neither the password nor anything derived from it is stored, so a
    /// forgotten password means the data is gone. The UI is expected to say so plainly
    /// before calling this.
    /// </remarks>
    public static Book Create(
        string databasePath,
        string password,
        BookKeyParameters? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrEmpty(password);

        string metadataPath = GetMetadataPath(databasePath);

        if (File.Exists(databasePath) || File.Exists(metadataPath))
        {
            throw new BookFileException($"A book already exists at '{databasePath}'.");
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        parameters ??= BookKeyDerivation.CreateParameters();
        BookKey key = BookKeyDerivation.DeriveKey(password, parameters);

        try
        {
            var book = new Book(databasePath, key, parameters);

            using (MyFinanceDbContext context = book.CreateContext())
            {
                context.Database.Migrate();

                // A book with no categories cannot categorize anything, so the first thing
                // the user would have to do is invent a chart of accounts. Seeded here rather
                // than on first use so it happens inside the same failure envelope as the
                // schema itself.
                DefaultCategories.Seed(context);

                SettingsService.WriteSchemaVersion(context, BookSchema.Current);
            }

            // Written only after the database is good, so a failure part-way through does
            // not leave a sidecar pointing at a database that was never created.
            File.WriteAllText(metadataPath, parameters.WithSchemaVersion(BookSchema.Current).ToJson());

            return book;
        }
        catch
        {
            key.Dispose();
            TryCleanUpPartialBook(databasePath, metadataPath);
            throw;
        }
    }

    /// <summary>Opens an existing book, throwing <see cref="IncorrectPasswordException"/> on a bad password.</summary>
    public static Book Open(string databasePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(password);

        if (!Exists(databasePath))
        {
            throw new BookNotFoundException(databasePath);
        }

        BookKeyParameters parameters = BookKeyParameters.FromJson(
            File.ReadAllText(GetMetadataPath(databasePath)));

        // Checked before the key is derived. The sidecar is plaintext, so a book from a newer
        // build can be refused without first spending half a second in Argon2id on a password
        // that was never going to help — and without EF looking at tables it may not
        // understand.
        if (BookSchema.Compare(parameters.SchemaVersionOrUnstamped) == BookSchema.State.TooNew)
        {
            throw new BookTooNewException(parameters.SchemaVersionOrUnstamped, BookSchema.Current);
        }

        BookKey key = BookKeyDerivation.DeriveKey(password, parameters);

        try
        {
            var factory = new EncryptedConnectionFactory(databasePath, key);

            // SQLCipher accepts any key at pragma time and only fails when it must actually
            // decrypt a page, so the unlock is not proven until something is read.
            using (SqliteConnection probe = factory.CreateOpenConnection())
            {
                if (!EncryptedConnectionFactory.CanRead(probe))
                {
                    throw new IncorrectPasswordException();
                }
            }

            var book = new Book(databasePath, key, parameters);

            try
            {
                ReconcileSchemaVersion(book, databasePath, metadataPath: GetMetadataPath(databasePath), parameters);
                return book;
            }
            catch
            {
                book.Dispose();
                throw;
            }
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Compares the database's own schema version against this build, repairing a lagging
    /// sidecar and refusing anything this build cannot honestly open.
    /// </summary>
    /// <remarks>
    /// The database is the authority. The sidecar can legitimately lag — a process can die
    /// between the two writes — but it cannot legitimately lead, so a sidecar claiming to be
    /// ahead of the database is a fault rather than something to quietly believe.
    /// </remarks>
    private static void ReconcileSchemaVersion(
        Book book,
        string databasePath,
        string metadataPath,
        BookKeyParameters parameters)
    {
        int recorded;

        using (MyFinanceDbContext context = book.CreateContext())
        {
            recorded = SettingsService.ReadSchemaVersion(context);
        }

        if (parameters.SchemaVersionOrUnstamped > recorded)
        {
            throw new BookUpgradeException(
                $"This book's key file says it is format {parameters.SchemaVersionOrUnstamped}, "
                + $"but the book itself is format {recorded}. One of the two files is from a "
                + "different book, or a copy was interrupted part-way.");
        }

        switch (BookSchema.Compare(recorded))
        {
            case BookSchema.State.TooNew:
                throw new BookTooNewException(recorded, BookSchema.Current);

            case BookSchema.State.NeedsUpgrade:
                // Not upgraded here. An upgrade rewrites the only copy of the user's records,
                // and they are entitled to be told before it starts and to know what was
                // protected first. Upgrade() is the deliberate second step.
                throw new BookUpgradeRequiredException(recorded, BookSchema.Current);

            default:
                if (parameters.SchemaVersion != recorded)
                {
                    // The sidecar was behind — repair it, so the cheap pre-key check keeps
                    // working next time. Unstamped books land here on their first open.
                    File.WriteAllText(metadataPath, parameters.WithSchemaVersion(recorded).ToJson());
                }

                break;
        }
    }

    /// <summary>
    /// Opens the book if it exists and creates it otherwise. Convenience for tests and for
    /// the first-run flow.
    /// </summary>
    public static Book OpenOrCreate(string databasePath, string password) =>
        Exists(databasePath) ? Open(databasePath, password) : Create(databasePath, password);

    /// <summary>
    /// Re-keys the book to a new password.
    /// </summary>
    /// <remarks>
    /// A fresh salt is generated rather than reused, so the new key is unrelated to the old
    /// one. <c>PRAGMA rekey</c> rewrites every page, so this is proportional to file size and
    /// the caller should treat it as a long-running operation.
    /// </remarks>
    public static void ChangePassword(string databasePath, string currentPassword, string newPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentException.ThrowIfNullOrEmpty(newPassword);

        // Proves the current password before touching anything.
        using (Book existing = Open(databasePath, currentPassword))
        {
        }

        string metadataPath = GetMetadataPath(databasePath);
        BookKeyParameters oldParameters = BookKeyParameters.FromJson(File.ReadAllText(metadataPath));
        BookKeyParameters newParameters = BookKeyDerivation.CreateParameters(
            oldParameters.Iterations,
            oldParameters.MemoryKib,
            oldParameters.Parallelism);

        using BookKey oldKey = BookKeyDerivation.DeriveKey(currentPassword, oldParameters);
        using BookKey newKey = BookKeyDerivation.DeriveKey(newPassword, newParameters);

        var factory = new EncryptedConnectionFactory(databasePath, oldKey);
        using (SqliteConnection connection = factory.CreateOpenConnection())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA rekey = \"x'{newKey.ToHex()}'\";";
            command.ExecuteNonQuery();
        }

        // Only now is the sidecar advanced: if the rekey threw, the old parameters still
        // describe the file on disk and the old password still works.
        File.WriteAllText(metadataPath, newParameters.ToJson());
    }

    /// <summary>
    /// Upgrades a book written by an earlier build to this build's schema, backing it up
    /// first and refusing to start if that backup cannot be written and verified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All-or-nothing by construction.</b> The migration is applied to a <em>copy</em>, and
    /// the copy replaces the original only once every migration has succeeded. EF Core wraps
    /// each individual migration in a transaction but not the sequence of them, so upgrading
    /// the file in place could leave a book three migrations into a five-migration run — a
    /// state nothing in the application knows how to read and nobody can inspect, because the
    /// file is encrypted.
    /// </para>
    /// <para>
    /// The backup is taken anyway, before any of that. Copy-and-replace protects against a
    /// migration that <em>fails</em>; it cannot protect against one that succeeds and is
    /// wrong, and this is the only moment at which the user's records can still be recovered.
    /// </para>
    /// </remarks>
    /// <returns>The path of the backup taken before the upgrade.</returns>
    public static string Upgrade(
        string databasePath,
        string password,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(password);

        if (!Exists(databasePath))
        {
            throw new BookNotFoundException(databasePath);
        }

        string metadataPath = GetMetadataPath(databasePath);
        BookKeyParameters parameters = BookKeyParameters.FromJson(File.ReadAllText(metadataPath));

        string backupPath;
        int from;

        progress?.Report(WorkProgress.Starting("Checking the book"));

        // Proves the password, and tells us what we are upgrading from, before anything is
        // written anywhere.
        using (Book book = OpenUnchecked(databasePath, password, parameters))
        {
            using (MyFinanceDbContext context = book.CreateContext())
            {
                from = SettingsService.ReadSchemaVersion(context);
            }

            if (BookSchema.Compare(from) == BookSchema.State.TooNew)
            {
                throw new BookTooNewException(from, BookSchema.Current);
            }

            if (BookSchema.Compare(from) == BookSchema.State.Current)
            {
                return string.Empty;
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(WorkProgress.Starting("Backing up first"));

            try
            {
                string directory = BackupService.DefaultDirectoryFor(databasePath);
                string name = BackupService.SuggestFileName(
                    Path.GetFileNameWithoutExtension(databasePath),
                    DateTimeOffset.UtcNow);

                // BackupService reads every archive back before accepting it, which is what
                // makes this a guarantee rather than a hope.
                backupPath = BackupService.Create(
                    book, Path.Combine(directory, name), progress, cancellationToken).Path;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new BookUpgradeException(
                    "The book could not be backed up, so it has not been updated. "
                    + "Free some disk space, or choose a different backup folder, and try again.",
                    backupPath: null,
                    ex);
            }
        }

        // The book is closed and its write-ahead log folded in by the backup above, so the
        // file on disk is now complete and safe to copy.
        string working = databasePath + ".upgrading";
        TryDelete(working);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(WorkProgress.Starting("Updating the book"));

            File.Copy(databasePath, working, overwrite: true);

            // The copy carries the same key: only the schema changes, never the encryption.
            using (var copy = new Book(working, BookKeyDerivation.DeriveKey(password, parameters), parameters))
            using (MyFinanceDbContext context = copy.CreateContext())
            {
                context.Database.Migrate();
                SettingsService.WriteSchemaVersion(context, BookSchema.Current);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(WorkProgress.Starting("Finishing"));

            // Only now does anything replace the original.
            File.Move(working, databasePath, overwrite: true);

            // Journal files belonging to the pre-upgrade database would be read alongside the
            // new one and would corrupt it.
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");

            File.WriteAllText(metadataPath, parameters.WithSchemaVersion(BookSchema.Current).ToJson());

            return backupPath;
        }
        catch (OperationCanceledException)
        {
            TryDelete(working);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(working);

            throw new BookUpgradeException(
                $"The book could not be updated from format {from} to {BookSchema.Current}, "
                + "and has been left exactly as it was.",
                backupPath,
                ex);
        }
    }

    /// <summary>
    /// Opens without comparing schema versions — the one path that must be able to open a
    /// book this build considers out of date, because it is the path that fixes it.
    /// </summary>
    private static Book OpenUnchecked(string databasePath, string password, BookKeyParameters parameters)
    {
        BookKey key = BookKeyDerivation.DeriveKey(password, parameters);

        try
        {
            var factory = new EncryptedConnectionFactory(databasePath, key);

            using (SqliteConnection probe = factory.CreateOpenConnection())
            {
                if (!EncryptedConnectionFactory.CanRead(probe))
                {
                    throw new IncorrectPasswordException();
                }
            }

            return new Book(databasePath, key, parameters);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryCleanUpPartialBook(string databasePath, string metadataPath)
    {
        foreach (string path in new[] { databasePath, metadataPath })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best effort: a leftover file is better than masking the original failure.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
