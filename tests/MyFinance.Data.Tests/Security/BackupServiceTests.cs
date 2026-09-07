using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Security;

namespace MyFinance.Data.Tests.Security;

public sealed class BackupServiceTests
{
    private static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "myfinance-backups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task SeedAsync(Book book, string accountName, decimal opening)
    {
        await using MyFinanceDbContext db = book.CreateContext();

        db.Accounts.Add(new Account
        {
            Name = accountName,
            Type = AccountType.Checking,
            OpeningBalance = Money.FromDecimal(opening),
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_backup_holds_both_halves_of_the_book()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book, "Checking", 100m);

        string destination = Path.Combine(TempDirectory(), "book.mfbak");
        BackupEntry entry = BackupService.Create(book, destination);

        File.Exists(destination).ShouldBeTrue();
        entry.SizeBytes.ShouldBeGreaterThan(0);

        using ZipArchive archive = ZipFile.OpenRead(destination);
        archive.GetEntry("book.mfdb").ShouldNotBeNull();
        archive.GetEntry("book.mfmeta").ShouldNotBeNull();
        archive.GetEntry("backup.json").ShouldNotBeNull();
    }

    /// <summary>
    /// The test the whole feature exists for: a restored book opens with the same password
    /// and holds the same records. Anything less is a file that only looks like a backup.
    /// </summary>
    [Fact]
    public async Task A_restored_book_opens_with_the_same_password_and_holds_the_same_data()
    {
        string destination = Path.Combine(TempDirectory(), "book.mfbak");

        using (var original = new TempBook())
        {
            using Book book = original.Create();
            await SeedAsync(book, "Everyday Checking", 861.57m);
            BackupService.Create(book, destination);
        }

        // Restored somewhere else entirely, as it would be on a new machine.
        string restoredPath = Path.Combine(TempDirectory(), "restored.mfdb");
        BackupService.Restore(destination, restoredPath);

        using Book restored = BookFileService.Open(restoredPath, TempBook.DefaultPassword);
        await using MyFinanceDbContext db = restored.CreateContext();

        Account account = await db.Accounts.SingleAsync();
        account.Name.ShouldBe("Everyday Checking");
        account.OpeningBalance.ShouldBe(Money.FromDecimal(861.57m));
    }

    /// <summary>
    /// Work done since the last checkpoint lives in the write-ahead log. A backup that
    /// skipped it would silently lose exactly the session the user is trying to protect.
    /// </summary>
    [Fact]
    public async Task Changes_made_moments_before_the_backup_are_in_it()
    {
        string destination = Path.Combine(TempDirectory(), "book.mfbak");

        using (var original = new TempBook())
        {
            using Book book = original.Create();
            await SeedAsync(book, "First", 10m);
            await SeedAsync(book, "Second", 20m);
            await SeedAsync(book, "Third", 30m);

            BackupService.Create(book, destination);
        }

        string restoredPath = Path.Combine(TempDirectory(), "restored.mfdb");
        BackupService.Restore(destination, restoredPath);

        using Book restored = BookFileService.Open(restoredPath, TempBook.DefaultPassword);
        await using MyFinanceDbContext db = restored.CreateContext();

        (await db.Accounts.CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task The_restored_book_still_refuses_the_wrong_password()
    {
        string destination = Path.Combine(TempDirectory(), "book.mfbak");

        using (var original = new TempBook())
        {
            using Book book = original.Create();
            await SeedAsync(book, "Checking", 1m);
            BackupService.Create(book, destination);
        }

        string restoredPath = Path.Combine(TempDirectory(), "restored.mfdb");
        BackupService.Restore(destination, restoredPath);

        Should.Throw<IncorrectPasswordException>(
            () => BookFileService.Open(restoredPath, "not the password"));
    }

    [Fact]
    public async Task Restoring_over_an_existing_book_is_refused_unless_asked_for()
    {
        string destination = Path.Combine(TempDirectory(), "book.mfbak");

        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            await SeedAsync(book, "Checking", 1m);
            BackupService.Create(book, destination);
        }

        BackupException error = Should.Throw<BackupException>(
            () => BackupService.Restore(destination, temp.DatabasePath));

        error.Message.ShouldContain("already a book");

        // And goes through when it is asked for.
        Should.NotThrow(() => BackupService.Restore(destination, temp.DatabasePath, overwrite: true));
    }

    /// <summary>
    /// A leftover write-ahead log from whatever used to sit at the destination would be read
    /// as part of the restored database and would corrupt it.
    /// </summary>
    [Fact]
    public async Task Restoring_clears_journal_files_left_by_a_previous_book()
    {
        string destination = Path.Combine(TempDirectory(), "book.mfbak");

        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            await SeedAsync(book, "Checking", 1m);
            BackupService.Create(book, destination);
        }

        File.WriteAllText(temp.DatabasePath + "-wal", "stale");
        File.WriteAllText(temp.DatabasePath + "-shm", "stale");

        BackupService.Restore(destination, temp.DatabasePath, overwrite: true);

        File.Exists(temp.DatabasePath + "-wal").ShouldBeFalse();
        File.Exists(temp.DatabasePath + "-shm").ShouldBeFalse();
    }

    [Fact]
    public async Task A_book_with_no_key_file_is_refused_rather_than_half_backed_up()
    {
        using var temp = new TempBook();

        using (Book book = temp.Create())
        {
            await SeedAsync(book, "Checking", 1m);
        }

        File.Delete(temp.MetadataPath);

        BackupException error = Should.Throw<BackupException>(
            () => BackupService.Create(temp.DatabasePath, Path.Combine(TempDirectory(), "b.mfbak")));

        error.Message.ShouldContain("key file");
    }

    [Fact]
    public async Task Backups_are_listed_newest_first()
    {
        string folder = TempDirectory();

        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book, "Checking", 1m);

        for (int i = 0; i < 3; i++)
        {
            BackupService.Create(book, Path.Combine(folder, $"backup-{i}.mfbak"));
            await Task.Delay(15);
        }

        IReadOnlyList<BackupEntry> listed = BackupService.List(folder);

        listed.Count.ShouldBe(3);
        listed.ShouldBeInOrder(SortDirection.Descending, Comparer<BackupEntry>.Create(
            (a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc)));
    }

    [Fact]
    public async Task A_damaged_file_in_the_folder_does_not_hide_the_good_ones()
    {
        string folder = TempDirectory();

        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book, "Checking", 1m);

        BackupService.Create(book, Path.Combine(folder, "good.mfbak"));
        File.WriteAllText(Path.Combine(folder, "broken.mfbak"), "this is not a zip");

        BackupService.List(folder).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Pruning_keeps_the_newest_and_removes_the_rest()
    {
        string folder = TempDirectory();

        using var temp = new TempBook("ledger.mfdb");
        using Book book = temp.Create();
        await SeedAsync(book, "Checking", 1m);

        for (int i = 0; i < 5; i++)
        {
            BackupService.Create(book, Path.Combine(folder, $"ledger-{i}.mfbak"));
            await Task.Delay(15);
        }

        int removed = BackupService.Prune(folder, "ledger", keep: 2);

        removed.ShouldBe(3);
        BackupService.List(folder).Count.ShouldBe(2);
    }

    /// <summary>Automatic deletion of the wrong file is worse than a full disk.</summary>
    [Fact]
    public async Task Pruning_never_touches_backups_of_a_different_book()
    {
        string folder = TempDirectory();

        using var first = new TempBook("ledger.mfdb");
        using Book a = first.Create();
        await SeedAsync(a, "Checking", 1m);
        BackupService.Create(a, Path.Combine(folder, "ledger.mfbak"));

        using var second = new TempBook("household.mfdb");
        using Book b = second.Create();
        await SeedAsync(b, "Checking", 1m);
        BackupService.Create(b, Path.Combine(folder, "household.mfbak"));

        BackupService.Prune(folder, "ledger", keep: 1).ShouldBe(0);
        BackupService.List(folder, "household").Count.ShouldBe(1);
    }

    [Fact]
    public void Pruning_to_fewer_than_one_does_nothing()
    {
        BackupService.Prune(TempDirectory(), "anything", keep: 0).ShouldBe(0);
    }

    [Fact]
    public void A_file_that_is_not_a_backup_is_reported_clearly()
    {
        string path = Path.Combine(TempDirectory(), "notes.mfbak");
        File.WriteAllText(path, "just some text");

        BackupException error = Should.Throw<BackupException>(() => BackupService.Inspect(path));
        error.Message.ShouldContain("notes.mfbak");
    }

    [Fact]
    public async Task A_backup_describes_the_book_it_came_from()
    {
        using var temp = new TempBook("household.mfdb");
        using Book book = temp.Create();
        await SeedAsync(book, "Checking", 1m);

        string destination = Path.Combine(TempDirectory(), "b.mfbak");
        BackupService.Create(book, destination);

        BackupEntry entry = BackupService.Inspect(destination);

        entry.Manifest.BookName.ShouldBe("household");
        entry.Manifest.Version.ShouldBe(BackupService.CurrentVersion);
        entry.Manifest.CreatedUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public void The_suggested_name_sorts_chronologically()
    {
        string earlier = BackupService.SuggestFileName(
            "household", new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        string later = BackupService.SuggestFileName(
            "household", new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

        string.CompareOrdinal(earlier, later).ShouldBeLessThan(0);
        earlier.ShouldEndWith(".mfbak");
    }

    [Fact]
    public void The_default_folder_sits_beside_the_book()
    {
        string directory = BackupService.DefaultDirectoryFor(
            Path.Combine("home", "books", "household.mfdb"));

        Path.GetFileName(directory).ShouldBe("Backups");
    }

    /// <summary>A half-written file must never be left where a good backup used to be.</summary>
    [Fact]
    public async Task An_earlier_backup_survives_a_failed_one()
    {
        string destination = Path.Combine(TempDirectory(), "book.mfbak");

        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book, "Checking", 1m);

        BackupService.Create(book, destination);
        long goodSize = new FileInfo(destination).Length;

        Should.Throw<BackupException>(
            () => BackupService.Create(Path.Combine(TempDirectory(), "missing.mfdb"), destination));

        new FileInfo(destination).Length.ShouldBe(goodSize);
        Should.NotThrow(() => BackupService.Inspect(destination));
    }
}
