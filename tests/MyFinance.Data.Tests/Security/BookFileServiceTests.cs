using System.Text;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Security;

namespace MyFinance.Data.Tests.Security;

public class BookFileServiceTests
{
    [Fact]
    public void Creating_a_book_writes_both_the_database_and_its_sidecar()
    {
        using var temp = new TempBook();

        using Book book = temp.Create();

        File.Exists(temp.DatabasePath).ShouldBeTrue();
        File.Exists(temp.MetadataPath).ShouldBeTrue();
        BookFileService.Exists(temp.DatabasePath).ShouldBeTrue();
    }

    [Fact]
    public void A_new_book_has_the_schema_applied()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();

        using MyFinanceDbContext context = book.CreateContext();

        context.Database.GetAppliedMigrations().ShouldNotBeEmpty();
        context.Accounts.Count().ShouldBe(0);
    }

    [Fact]
    public void The_database_file_is_encrypted_on_disk()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account { Name = "Everyday Checking", Type = AccountType.Checking });
            context.SaveChanges();
        }

        byte[] header = new byte[16];
        using (FileStream stream = File.OpenRead(temp.DatabasePath))
        {
            stream.ReadExactly(header);
        }

        // A plaintext SQLite database always begins with this magic string. SQLCipher
        // encrypts from the very first byte, so its absence is direct evidence the file is
        // not readable as an ordinary database.
        Encoding.ASCII.GetString(header).ShouldNotStartWith("SQLite format 3");
    }

    [Fact]
    public void Account_names_do_not_appear_in_plaintext_on_disk()
    {
        const string SecretName = "Rainy Day Savings";

        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account { Name = SecretName, Type = AccountType.Savings });
            context.SaveChanges();
        }

        byte[] bytes = File.ReadAllBytes(temp.DatabasePath);

        bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(SecretName)).ShouldBe(-1);
    }

    [Fact]
    public void A_book_reopens_with_the_correct_password()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account { Name = "Holiday Fund", Type = AccountType.Savings });
            context.SaveChanges();
        }

        using Book reopened = temp.Open();
        using MyFinanceDbContext reopenedContext = reopened.CreateContext();

        reopenedContext.Accounts.Single().Name.ShouldBe("Holiday Fund");
    }

    [Fact]
    public void The_wrong_password_is_rejected()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        Should.Throw<IncorrectPasswordException>(() => temp.Open("not the password"));
    }

    [Fact]
    public void A_password_differing_by_one_character_is_rejected()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        Should.Throw<IncorrectPasswordException>(
            () => temp.Open(TempBook.DefaultPassword + "!"));
    }

    [Fact]
    public void An_empty_password_is_rejected_at_creation()
    {
        using var temp = new TempBook();

        Should.Throw<ArgumentException>(() => temp.Create(string.Empty));
    }

    [Fact]
    public void Opening_a_missing_book_reports_it_as_missing_not_as_a_bad_password()
    {
        using var temp = new TempBook();

        Should.Throw<BookNotFoundException>(() => temp.Open());
    }

    [Fact]
    public void Opening_a_book_whose_sidecar_is_missing_is_reported_as_missing()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        File.Delete(temp.MetadataPath);

        // The salt is gone, so the key cannot be re-derived at all. Losing the sidecar is as
        // fatal as losing the password; backups must copy both files.
        Should.Throw<BookNotFoundException>(() => temp.Open());
    }

    [Fact]
    public void Creating_over_an_existing_book_is_refused()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        Should.Throw<BookFileException>(() => temp.Create());
    }

    [Fact]
    public void A_failed_creation_leaves_no_half_written_book_behind()
    {
        using var temp = new TempBook();

        // Create the book file first, then aim a new book at a path *underneath* it. A file
        // cannot act as a directory, so directory creation fails part-way through Create.
        using (Book book = temp.Create())
        {
        }

        string impossible = Path.Combine(temp.DatabasePath, "child.mfdb");

        Should.Throw<IOException>(() =>
            BookFileService.Create(impossible, TempBook.DefaultPassword, TempBook.FastParameters()));

        File.Exists(impossible).ShouldBeFalse();
        File.Exists(BookFileService.GetMetadataPath(impossible)).ShouldBeFalse();
    }

    // -- Re-keying ----------------------------------------------------------------------

    [Fact]
    public void Changing_the_password_switches_which_password_opens_the_book()
    {
        const string NewPassword = "a brand new passphrase";

        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account { Name = "Everyday Rewards Card", Type = AccountType.CreditCard });
            context.SaveChanges();
        }

        BookFileService.ChangePassword(temp.DatabasePath, TempBook.DefaultPassword, NewPassword);

        Should.Throw<IncorrectPasswordException>(() => temp.Open());

        using Book reopened = temp.Open(NewPassword);
        using MyFinanceDbContext reopenedContext = reopened.CreateContext();
        reopenedContext.Accounts.Single().Name.ShouldBe("Everyday Rewards Card");
    }

    [Fact]
    public void Changing_the_password_generates_a_fresh_salt()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        string saltBefore = BookKeyParameters.FromJson(File.ReadAllText(temp.MetadataPath)).SaltBase64;

        BookFileService.ChangePassword(temp.DatabasePath, TempBook.DefaultPassword, "second password");

        string saltAfter = BookKeyParameters.FromJson(File.ReadAllText(temp.MetadataPath)).SaltBase64;

        // A reused salt would make the new key a deterministic function of the old one.
        saltAfter.ShouldNotBe(saltBefore);
    }

    [Fact]
    public void Changing_the_password_with_the_wrong_current_password_is_refused()
    {
        using var temp = new TempBook();
        using (Book book = temp.Create())
        {
        }

        Should.Throw<IncorrectPasswordException>(
            () => BookFileService.ChangePassword(temp.DatabasePath, "wrong", "new"));

        // The original password must still work: a rejected attempt changes nothing.
        using Book stillOpens = temp.Open();
        stillOpens.ShouldNotBeNull();
    }

    // -- Round-tripping data -------------------------------------------------------------

    [Fact]
    public void Money_survives_a_save_and_reload_exactly()
    {
        using var temp = new TempBook();
        Money opening = Money.FromDecimal(143957.89m);

        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account
            {
                Name = "Rainy Day Savings",
                Type = AccountType.Savings,
                OpeningBalance = opening,
            });
            context.SaveChanges();
        }

        using Book reopened = temp.Open();
        using MyFinanceDbContext reopenedContext = reopened.CreateContext();

        reopenedContext.Accounts.Single().OpeningBalance.ShouldBe(opening);
    }

    [Fact]
    public void A_negative_balance_round_trips_with_its_sign()
    {
        using var temp = new TempBook();

        using (Book book = temp.Create())
        {
            using MyFinanceDbContext context = book.CreateContext();
            context.Accounts.Add(new Account
            {
                Name = "Everyday Checking",
                Type = AccountType.Checking,
                OpeningBalance = Money.FromDecimal(-28117.70m),
            });
            context.SaveChanges();
        }

        using Book reopened = temp.Open();
        using MyFinanceDbContext reopenedContext = reopened.CreateContext();

        reopenedContext.Accounts.Single().OpeningBalance.ToDecimal().ShouldBe(-28117.70m);
    }

    [Fact]
    public void A_disposed_book_will_not_hand_out_further_contexts()
    {
        using var temp = new TempBook();
        Book book = temp.Create();
        book.Dispose();

        Should.Throw<ObjectDisposedException>(() => book.CreateContext());
    }
}
