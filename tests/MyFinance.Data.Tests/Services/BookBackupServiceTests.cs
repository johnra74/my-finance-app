using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Security;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

public sealed class BookBackupServiceTests
{
    private static async Task SeedAsync(Book book)
    {
        await using MyFinanceDbContext db = book.CreateContext();

        db.Accounts.Add(new Account
        {
            Name = "Checking",
            Type = AccountType.Checking,
            OpeningBalance = Money.FromDecimal(10m),
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// There is no password recovery and no copy of a book anywhere else, so a book nobody
    /// has configured must still be backing itself up.
    /// </summary>
    [Fact]
    public async Task A_new_book_backs_itself_up_by_default()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        var service = new BookBackupService(book);

        BackupPreferences preferences = await service.GetPreferencesAsync();

        preferences.Automatic.ShouldBeTrue();
        preferences.Keep.ShouldBeGreaterThan(1);
        preferences.Directory.ShouldBeNull();
    }

    [Fact]
    public async Task Preferences_are_remembered()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        var service = new BookBackupService(book);

        await service.SavePreferencesAsync(new BackupPreferences
        {
            Automatic = false,
            Keep = 3,
            Directory = Path.Combine(Path.GetTempPath(), "elsewhere"),
        });

        BackupPreferences read = await service.GetPreferencesAsync();

        read.Automatic.ShouldBeFalse();
        read.Keep.ShouldBe(3);
        read.Directory.ShouldEndWith("elsewhere");
    }

    [Fact]
    public async Task Backing_up_writes_into_a_folder_beside_the_book()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book);

        var service = new BookBackupService(book);
        BackupEntry entry = await service.BackUpNowAsync(book);

        File.Exists(entry.Path).ShouldBeTrue();
        Path.GetFileName(Path.GetDirectoryName(entry.Path)).ShouldBe("Backups");

        (await service.ListAsync(book)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Closing_the_book_takes_a_backup_when_that_is_switched_on()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book);

        var service = new BookBackupService(book);
        BackupEntry? entry = await service.BackUpOnCloseAsync(book);

        entry.ShouldNotBeNull();
        File.Exists(entry.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task Closing_the_book_takes_no_backup_when_that_is_switched_off()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book);

        var service = new BookBackupService(book);
        await service.SavePreferencesAsync(new BackupPreferences { Automatic = false });

        (await service.BackUpOnCloseAsync(book)).ShouldBeNull();
        (await service.ListAsync(book)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Automatic_backups_are_pruned_to_the_number_asked_for()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book);

        var service = new BookBackupService(book);
        await service.SavePreferencesAsync(new BackupPreferences { Keep = 2 });

        for (int i = 0; i < 4; i++)
        {
            await service.BackUpNowAsync(book);
            await Task.Delay(1100); // The suggested name is per-minute; force distinct files.
        }

        (await service.ListAsync(book)).Count.ShouldBeLessThanOrEqualTo(2);
    }

    /// <summary>
    /// A backup the user deliberately put somewhere of their own is theirs. Nothing here
    /// starts deleting from a folder they chose.
    /// </summary>
    [Fact]
    public async Task A_backup_saved_somewhere_chosen_is_never_pruned()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book);

        string elsewhere = Path.Combine(
            Path.GetTempPath(), "myfinance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);

        var service = new BookBackupService(book);
        await service.SavePreferencesAsync(new BackupPreferences { Keep = 1 });

        for (int i = 0; i < 3; i++)
        {
            await service.BackUpNowAsync(book, Path.Combine(elsewhere, $"copy-{i}.mfbak"));
        }

        BackupService.List(elsewhere).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_nonsense_retention_setting_is_clamped_rather_than_obeyed()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        var service = new BookBackupService(book);

        await service.SavePreferencesAsync(new BackupPreferences { Keep = -5 });

        (await service.GetPreferencesAsync()).EffectiveKeep.ShouldBe(1);
    }

    [Fact]
    public async Task A_chosen_backup_folder_is_used_instead_of_the_default()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();
        await SeedAsync(book);

        string elsewhere = Path.Combine(
            Path.GetTempPath(), "myfinance-tests", Guid.NewGuid().ToString("N"));

        var service = new BookBackupService(book);
        await service.SavePreferencesAsync(new BackupPreferences { Directory = elsewhere });

        BackupEntry entry = await service.BackUpNowAsync(book);

        entry.Path.ShouldStartWith(elsewhere);
        (await service.GetDirectoryAsync(book)).ShouldBe(elsewhere);
    }
}
