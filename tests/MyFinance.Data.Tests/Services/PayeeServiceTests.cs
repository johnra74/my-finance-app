using Microsoft.EntityFrameworkCore;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

public sealed class PayeeServiceTests
{
    [Fact]
    public async Task A_payee_is_created_once_and_found_again()
    {
        using var book = new BookHarness();

        int first = await book.Payees.FindOrCreateAsync("Woodgrove Mortgage");
        int second = await book.Payees.FindOrCreateAsync("woodgrove   mortgage!");

        second.ShouldBe(first);

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Payees.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_blank_payee_name_is_refused()
    {
        using var book = new BookHarness();

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Payees.FindOrCreateAsync("   "));

        thrown.Errors.ShouldContain(e => e.Code == PayeeService.NameRequired);
    }

    [Fact]
    public async Task A_payee_can_be_renamed()
    {
        using var book = new BookHarness();
        int id = await book.Payees.FindOrCreateAsync("Woodgrove");

        await book.Payees.RenameAsync(id, "Woodgrove Home Loans");

        (await book.Payees.FindByNameAsync("woodgrove home loans"))!.Id.ShouldBe(id);
    }

    [Fact]
    public async Task Renaming_onto_an_existing_payee_is_refused()
    {
        using var book = new BookHarness();
        await book.Payees.FindOrCreateAsync("Fabrikam Fuel");
        int second = await book.Payees.FindOrCreateAsync("Adventure Works");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Payees.RenameAsync(second, "Fabrikam Fuel"));

        thrown.Errors.ShouldContain(e => e.Code == PayeeService.NameDuplicate);
    }

    [Fact]
    public async Task An_unused_payee_can_be_deleted()
    {
        using var book = new BookHarness();
        int id = await book.Payees.FindOrCreateAsync("Typo Ltd");

        await book.Payees.DeleteAsync(id);

        (await book.Payees.GetAllAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_payee_with_transactions_cannot_be_deleted()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        await book.AddTransactionAsync(account, -10m, payee: "Fabrikam Fuel");

        int id = (await book.Payees.FindByNameAsync("Fabrikam Fuel"))!.Id;

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Payees.DeleteAsync(id));

        thrown.Errors.ShouldContain(e => e.Code == PayeeService.PayeeInUse);
    }

    [Fact]
    public async Task Merging_repoints_the_transactions_and_keeps_the_old_name_as_an_alias()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.AddTransactionAsync(account, -10m, payee: "SQ BLUE BOTTLE 1234");
        await book.AddTransactionAsync(account, -12m, payee: "Blue Bottle Coffee");

        int source = (await book.Payees.FindByNameAsync("SQ BLUE BOTTLE 1234"))!.Id;
        int target = (await book.Payees.FindByNameAsync("Blue Bottle Coffee"))!.Id;

        await book.Payees.MergeAsync(source, target);

        IReadOnlyList<PayeeListItem> payees = await book.Payees.GetAllAsync();
        payees.Count.ShouldBe(1);
        payees[0].Id.ShouldBe(target);
        payees[0].UseCount.ShouldBe(2);

        // Keeping the source descriptor as an alias is what stops the next download from
        // recreating the duplicate that was just merged away.
        await using MyFinanceDbContext db = book.CreateContext();
        (await db.PayeeAliases.CountAsync()).ShouldBe(1);
        (await db.PayeeAliases.SingleAsync()).NormalizedPattern.ShouldBe("SQ BLUE BOTTLE 1234");
    }

    [Fact]
    public async Task Merging_a_payee_into_itself_is_refused()
    {
        using var book = new BookHarness();
        int id = await book.Payees.FindOrCreateAsync("Fabrikam Fuel");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Payees.MergeAsync(id, id));

        thrown.Errors.ShouldContain(e => e.Code == PayeeService.NotFound);
    }

    [Fact]
    public async Task Payee_names_come_back_sorted_for_autocomplete()
    {
        using var book = new BookHarness();
        await book.Payees.FindOrCreateAsync("Fabrikam Fuel");
        await book.Payees.FindOrCreateAsync("Adventure Works");
        await book.Payees.FindOrCreateAsync("Contoso Grocers");

        IReadOnlyList<string> names = await book.Payees.GetNamesAsync();

        names.ShouldBe(["Adventure Works", "Contoso Grocers", "Fabrikam Fuel"]);
    }

    [Fact]
    public async Task Looking_up_a_payee_that_does_not_exist_returns_nothing()
    {
        using var book = new BookHarness();

        (await book.Payees.FindByNameAsync("Nobody")).ShouldBeNull();
        (await book.Payees.FindByNameAsync(null)).ShouldBeNull();
        (await book.Payees.FindByNameAsync("  ")).ShouldBeNull();
    }
}
