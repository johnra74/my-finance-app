using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Data.Security;

namespace MyFinance.Data.Tests.Security;

/// <summary>
/// What happens when a book meets a build that is not the one that wrote it.
/// </summary>
/// <remarks>
/// The consequences here are worse than the code suggests: one encrypted file, often the only
/// copy, with no password recovery and no server-side backup. A migration that half-applies is
/// not something a user can inspect or repair.
/// </remarks>
public class SchemaUpgradeTests
{
    /// <summary>
    /// Rewrites the sidecar's schema stamp, standing in for a book written by another build.
    /// </summary>
    private static void StampSidecar(string metadataPath, int? version)
    {
        JsonObject json = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();

        if (version is int value)
        {
            json["schemaVersion"] = value;
        }
        else
        {
            json.Remove("schemaVersion");
        }

        File.WriteAllText(metadataPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void StampDatabase(TempBook temp, int? version)
    {
        using Book book = temp.Open();
        using MyFinanceDbContext context = book.CreateContext();

        AppSetting? row = context.AppSettings.FirstOrDefault(s => s.Key == "schema.version");

        if (version is int value)
        {
            if (row is null)
            {
                context.AppSettings.Add(new AppSetting { Key = "schema.version", Value = value.ToString() });
            }
            else
            {
                row.Value = value.ToString();
            }
        }
        else if (row is not null)
        {
            context.AppSettings.Remove(row);
        }

        context.SaveChanges();
    }

    // -- T001: the version this build claims ------------------------------------------

    [Fact]
    public void The_current_schema_version_matches_the_migration_count()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        using MyFinanceDbContext context = book.CreateContext();

        // Adding a migration without incrementing BookSchema.Current would leave every new
        // book claiming to be older than it is, and the newer-book refusal would never fire.
        BookSchema.Current.ShouldBe(context.Database.GetMigrations().Count());
    }

    [Theory]
    [InlineData(BookSchema.Current - 1, BookSchema.State.NeedsUpgrade)]
    [InlineData(BookSchema.Current, BookSchema.State.Current)]
    [InlineData(BookSchema.Current + 1, BookSchema.State.TooNew)]
    public void A_version_is_compared_against_this_build(int version, BookSchema.State expected) =>
        BookSchema.Compare(version).ShouldBe(expected);

    // -- T003 / T004: both copies are stamped ------------------------------------------

    [Fact]
    public void A_new_book_records_its_schema_version_in_both_places()
    {
        using var temp = new TempBook();

        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.AppSettings.Single(s => s.Key == "schema.version").Value
                .ShouldBe(BookSchema.Current.ToString());
        }

        BookKeyParameters parameters = BookKeyParameters.FromJson(File.ReadAllText(temp.MetadataPath));
        parameters.SchemaVersion.ShouldBe(BookSchema.Current);
    }

    [Fact]
    public void A_sidecar_round_trips_its_schema_version()
    {
        BookKeyParameters parameters = TempBook.FastParameters().WithSchemaVersion(7);

        BookKeyParameters restored = BookKeyParameters.FromJson(parameters.ToJson());

        restored.SchemaVersion.ShouldBe(7);
        restored.SchemaVersionOrUnstamped.ShouldBe(7);
    }

    [Fact]
    public void A_sidecar_without_a_schema_version_reads_as_the_current_one()
    {
        // Books written before versioning shipped carry no stamp. Treating that as "unknown"
        // would make this feature's first act be to refuse every book already on disk.
        BookKeyParameters parameters = TempBook.FastParameters();

        parameters.SchemaVersion.ShouldBeNull();
        parameters.SchemaVersionOrUnstamped.ShouldBe(BookSchema.Unstamped);
    }

    [Fact]
    public void A_book_with_no_recorded_version_is_read_as_the_schema_that_predates_stamping()
    {
        // The durable invariant: a book written before versioning existed is never *refused*.
        // It is recognised as BookSchema.Unstamped and upgraded like any other old book.
        //
        // This used to open straight through, because Unstamped and Current were the same
        // number. They are not any more — a migration has shipped since — and needing an
        // upgrade is the correct answer, not a regression.
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampDatabase(temp, null);
        StampSidecar(temp.MetadataPath, null);

        var required = Should.Throw<BookUpgradeRequiredException>(() => temp.Open());
        required.BookVersion.ShouldBe(BookSchema.Unstamped);
        required.TargetVersion.ShouldBe(BookSchema.Current);
    }

    [Fact]
    public void An_unstamped_book_upgrades_and_ends_up_stamped_in_both_places()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampDatabase(temp, null);
        StampSidecar(temp.MetadataPath, null);

        BookFileService.Upgrade(temp.DatabasePath, TempBook.DefaultPassword);

        BookKeyParameters parameters = BookKeyParameters.FromJson(File.ReadAllText(temp.MetadataPath));
        parameters.SchemaVersion.ShouldBe(BookSchema.Current);

        using Book reopened = temp.Open();
        using MyFinanceDbContext context = reopened.CreateContext();
        context.AppSettings.Single(s => s.Key == "schema.version").Value
            .ShouldBe(BookSchema.Current.ToString());
    }

    // -- T005 / T006: refusing a book from the future ----------------------------------

    [Fact]
    public void A_book_from_a_newer_build_is_refused_before_the_key_is_derived()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampSidecar(temp.MetadataPath, BookSchema.Current + 1);

        // Refused on the wrong password too, which is the proof that no key was derived: the
        // sidecar check runs before Argon2id, so the password never gets as far as mattering.
        var refused = Should.Throw<BookTooNewException>(
            () => BookFileService.Open(temp.DatabasePath, "not the password"));

        refused.BookVersion.ShouldBe(BookSchema.Current + 1);
        refused.SupportedVersion.ShouldBe(BookSchema.Current);
    }

    [Fact]
    public void A_newer_schema_in_the_database_is_refused()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        // The sidecar is left honest, so this can only be caught by reading the database.
        StampDatabase(temp, BookSchema.Current + 2);
        StampSidecar(temp.MetadataPath, BookSchema.Current);

        Should.Throw<BookTooNewException>(() => temp.Open())
            .BookVersion.ShouldBe(BookSchema.Current + 2);
    }

    [Fact]
    public void A_refused_book_is_byte_for_byte_unchanged_afterwards()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account { Name = "Everyday", Type = AccountType.Checking });
            context.SaveChanges();
        }

        StampSidecar(temp.MetadataPath, BookSchema.Current + 1);

        byte[] before = File.ReadAllBytes(temp.DatabasePath);
        Should.Throw<BookTooNewException>(() => temp.Open());

        File.ReadAllBytes(temp.DatabasePath).ShouldBe(before);
    }

    // -- T007: the two copies disagreeing ----------------------------------------------

    [Fact]
    public void A_stale_sidecar_version_is_repaired_from_the_database()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        // The sidecar may legitimately lag: a process can die between the two writes.
        StampSidecar(temp.MetadataPath, BookSchema.Current - 1);

        using (Book reopened = temp.Open())
        {
            reopened.ShouldNotBeNull();
        }

        BookKeyParameters parameters = BookKeyParameters.FromJson(File.ReadAllText(temp.MetadataPath));
        parameters.SchemaVersion.ShouldBe(BookSchema.Current);
    }

    [Fact]
    public void A_sidecar_claiming_a_newer_version_than_the_database_is_reported()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        // The sidecar cannot legitimately lead. Either the two files are from different
        // books, or a copy was interrupted — both worth saying out loud.
        StampDatabase(temp, BookSchema.Current - 1);
        StampSidecar(temp.MetadataPath, BookSchema.Current);

        Should.Throw<BookUpgradeException>(() => temp.Open())
            .Message.ShouldContain("key file");
    }

    // -- T008: an older book is announced, not silently upgraded -----------------------

    [Fact]
    public void An_older_book_is_reported_as_needing_an_upgrade_rather_than_upgraded_silently()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampDatabase(temp, BookSchema.Current - 1);
        StampSidecar(temp.MetadataPath, BookSchema.Current - 1);

        var required = Should.Throw<BookUpgradeRequiredException>(() => temp.Open());

        required.BookVersion.ShouldBe(BookSchema.Current - 1);
        required.TargetVersion.ShouldBe(BookSchema.Current);
    }

    // -- T009 / T010: upgrading safely -------------------------------------------------

    [Fact]
    public void An_upgrade_takes_a_verified_backup_first()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampDatabase(temp, BookSchema.Current - 1);
        StampSidecar(temp.MetadataPath, BookSchema.Current - 1);

        string backup = BookFileService.Upgrade(temp.DatabasePath, TempBook.DefaultPassword);

        backup.ShouldNotBeNullOrEmpty();
        File.Exists(backup).ShouldBeTrue();

        // Not merely written — readable, and describing this book.
        BackupService.Inspect(backup).Manifest.BookName
            .ShouldBe(Path.GetFileNameWithoutExtension(temp.DatabasePath));
    }

    [Fact]
    public void A_completed_upgrade_advances_both_version_copies()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampDatabase(temp, BookSchema.Current - 1);
        StampSidecar(temp.MetadataPath, BookSchema.Current - 1);

        BookFileService.Upgrade(temp.DatabasePath, TempBook.DefaultPassword);

        BookKeyParameters parameters = BookKeyParameters.FromJson(File.ReadAllText(temp.MetadataPath));
        parameters.SchemaVersion.ShouldBe(BookSchema.Current);

        using Book upgraded = temp.Open();
        using MyFinanceDbContext context = upgraded.CreateContext();
        context.AppSettings.Single(s => s.Key == "schema.version").Value
            .ShouldBe(BookSchema.Current.ToString());
    }

    [Fact]
    public void An_upgrade_keeps_every_row_the_book_already_held()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account { Name = "Everyday", Type = AccountType.Checking });
            context.SaveChanges();
        }

        StampDatabase(temp, BookSchema.Current - 1);
        StampSidecar(temp.MetadataPath, BookSchema.Current - 1);

        BookFileService.Upgrade(temp.DatabasePath, TempBook.DefaultPassword);

        using Book upgraded = temp.Open();
        using MyFinanceDbContext reopened = upgraded.CreateContext();
        reopened.Accounts.Single().Name.ShouldBe("Everyday");
    }

    [Fact]
    public void An_upgrade_with_the_wrong_password_changes_nothing()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampDatabase(temp, BookSchema.Current - 1);
        StampSidecar(temp.MetadataPath, BookSchema.Current - 1);

        byte[] before = File.ReadAllBytes(temp.DatabasePath);

        Should.Throw<IncorrectPasswordException>(
            () => BookFileService.Upgrade(temp.DatabasePath, "not the password"));

        File.ReadAllBytes(temp.DatabasePath).ShouldBe(before);
    }

    [Fact]
    public void An_upgrade_interrupted_part_way_leaves_the_book_as_it_was()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account { Name = "Everyday", Type = AccountType.Checking });
            context.SaveChanges();
        }

        StampDatabase(temp, BookSchema.Current - 1);
        StampSidecar(temp.MetadataPath, BookSchema.Current - 1);

        byte[] before = File.ReadAllBytes(temp.DatabasePath);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(
            () => BookFileService.Upgrade(temp.DatabasePath, TempBook.DefaultPassword, null, cancelled.Token));

        // The migration runs against a copy, so the original is untouched whatever happens to
        // that copy — and no half-finished working file is left behind.
        File.ReadAllBytes(temp.DatabasePath).ShouldBe(before);
        File.Exists(temp.DatabasePath + ".upgrading").ShouldBeFalse();
    }

    [Fact]
    public void Upgrading_a_book_that_is_already_current_does_nothing()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        BookFileService.Upgrade(temp.DatabasePath, TempBook.DefaultPassword).ShouldBeEmpty();
    }

    [Fact]
    public void Upgrading_a_book_from_a_newer_build_is_refused()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampDatabase(temp, BookSchema.Current + 1);

        Should.Throw<BookTooNewException>(
            () => BookFileService.Upgrade(temp.DatabasePath, TempBook.DefaultPassword));
    }

    // -- T011: a failure that says what to do next -------------------------------------

    [Fact]
    public void A_failed_upgrade_names_the_backup_to_restore()
    {
        var failure = new BookUpgradeException("It did not work.", @"C:\Books\Backups\book-2026.mfbak");

        failure.BackupPath.ShouldBe(@"C:\Books\Backups\book-2026.mfbak");
        failure.Message.ShouldContain("book-2026.mfbak");
    }

    // -- T012: progress ----------------------------------------------------------------

    [Fact]
    public void An_upgrade_reports_progress_while_it_runs()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        StampDatabase(temp, BookSchema.Current - 1);
        StampSidecar(temp.MetadataPath, BookSchema.Current - 1);

        var stages = new List<string>();
        var progress = new Progress<Core.Progress.WorkProgress>(p => stages.Add(p.Stage));

        BookFileService.Upgrade(temp.DatabasePath, TempBook.DefaultPassword, progress);

        // Progress<T> posts asynchronously; the assertion is that it reported at all, not the
        // exact sequence, which ProgressAndCancellationTests already covers in detail.
        Thread.Sleep(50);
        stages.ShouldNotBeEmpty();
    }

    // -- T013: composing with a backup taken under an older schema ---------------------

    [Fact]
    public void A_book_restored_from_an_older_backup_upgrades_on_first_open()
    {
        using var temp = new TempBook();
        using var destination = new TempBook("restored.mfdb");

        string archive;

        using (Book book = temp.Create())
        {
            using (MyFinanceDbContext context = book.CreateContext())
            {
                context.Accounts.Add(new Account { Name = "Everyday", Type = AccountType.Checking });
                context.SaveChanges();
            }

            archive = BackupService.Create(
                book, Path.Combine(Path.GetDirectoryName(temp.DatabasePath)!, "older.mfbak")).Path;
        }

        BackupService.Restore(archive, destination.DatabasePath);

        // Age the restored copy, as a backup taken under a previous schema would be.
        StampDatabase(destination, BookSchema.Current - 1);
        StampSidecar(destination.MetadataPath, BookSchema.Current - 1);

        Should.Throw<BookUpgradeRequiredException>(() => destination.Open());

        BookFileService.Upgrade(destination.DatabasePath, TempBook.DefaultPassword);

        using Book upgraded = destination.Open();
        using MyFinanceDbContext reopened = upgraded.CreateContext();
        reopened.Accounts.Single().Name.ShouldBe("Everyday");
    }
}
