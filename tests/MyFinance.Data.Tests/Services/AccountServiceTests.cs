using MyFinance.Core.Accounts;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

public sealed class AccountServiceTests
{
    [Fact]
    public async Task An_account_can_be_created_and_read_back()
    {
        using var book = new BookHarness();

        int id = await book.Accounts.CreateAsync(new AccountDraft
        {
            Name = "Everyday Checking",
            Type = AccountType.Checking,
            Institution = "Contoso Bank",
            AccountNumberMasked = "XXXXXXXX6464",
            OpeningBalance = Money.FromDecimal(1_000m),
        });

        Account? account = await book.Accounts.FindAsync(id);

        account.ShouldNotBeNull();
        account.Name.ShouldBe("Everyday Checking");
        account.Institution.ShouldBe("Contoso Bank");
        account.OpeningBalance.ShouldBe(Money.FromDecimal(1_000m));
        account.Group.ShouldBe(AccountGroup.Bank);
    }

    [Fact]
    public async Task Two_accounts_cannot_share_a_name()
    {
        using var book = new BookHarness();
        await book.AddAccountAsync("Savings");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.AddAccountAsync("Savings"));

        thrown.Errors.ShouldContain(e => e.Code == AccountService.NameDuplicate);
    }

    [Fact]
    public async Task A_blank_name_is_refused()
    {
        using var book = new BookHarness();

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Accounts.CreateAsync(new AccountDraft { Name = "   " }));

        thrown.Errors.ShouldContain(e => e.Code == AccountService.NameRequired);
    }

    [Fact]
    public async Task Editing_an_account_keeps_the_edits()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Old name");

        await book.Accounts.UpdateAsync(new AccountDraft
        {
            Id = id,
            Name = "New name",
            Type = AccountType.Savings,
            Institution = "Holiday Fund",
            OpeningBalance = Money.FromDecimal(250m),
            IsFavorite = true,
        });

        Account? account = await book.Accounts.FindAsync(id);

        account.ShouldNotBeNull();
        account.Name.ShouldBe("New name");
        account.Institution.ShouldBe("Holiday Fund");
        account.IsFavorite.ShouldBeTrue();
        account.OpeningBalance.ShouldBe(Money.FromDecimal(250m));
    }

    [Fact]
    public async Task Renaming_onto_another_accounts_name_is_refused()
    {
        using var book = new BookHarness();
        await book.AddAccountAsync("Checking");
        int second = await book.AddAccountAsync("Savings");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Accounts.UpdateAsync(new AccountDraft { Id = second, Name = "Checking" }));

        thrown.Errors.ShouldContain(e => e.Code == AccountService.NameDuplicate);
    }

    [Fact]
    public async Task An_account_keeps_its_own_name_when_edited()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Checking");

        // The duplicate check has to exclude the row being edited, or changing anything
        // other than the name would be impossible.
        await book.Accounts.UpdateAsync(new AccountDraft
        {
            Id = id,
            Name = "Checking",
            Institution = "Contoso Bank",
        });

        Account? account = await book.Accounts.FindAsync(id);
        account.ShouldNotBeNull();
        account.Institution.ShouldBe("Contoso Bank");
    }

    [Fact]
    public async Task The_type_is_frozen_once_transactions_exist()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Checking", AccountType.Checking);
        await book.AddTransactionAsync(id, -25m);

        await book.Accounts.UpdateAsync(new AccountDraft
        {
            Id = id,
            Name = "Checking",
            Type = AccountType.CreditCard,
        });

        Account? account = await book.Accounts.FindAsync(id);

        // Flipping the type would reverse the meaning of the sign on everything already
        // recorded, so the edit is ignored rather than silently corrupting the history.
        account.ShouldNotBeNull();
        account.Type.ShouldBe(AccountType.Checking);
    }

    [Fact]
    public async Task Closing_an_account_hides_it_without_losing_it()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Old card", AccountType.CreditCard);

        await book.Accounts.SetClosedAsync(id, isClosed: true);

        (await book.Accounts.GetAllAsync()).ShouldBeEmpty();
        (await book.Accounts.GetAllAsync(includeClosed: true)).Count.ShouldBe(1);

        await book.Accounts.SetClosedAsync(id, isClosed: false);
        (await book.Accounts.GetAllAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_an_account_takes_its_register_with_it()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Scratch");
        await book.AddTransactionAsync(id, -10m);
        await book.AddTransactionAsync(id, -20m);

        (await book.Accounts.CountTransactionsAsync(id)).ShouldBe(2);

        await book.Accounts.DeleteAsync(id);

        (await book.Accounts.FindAsync(id)).ShouldBeNull();

        await using MyFinanceDbContext db = book.CreateContext();
        db.Transactions.Count().ShouldBe(0);
        db.TransactionSplits.Count().ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_an_account_removes_the_far_leg_of_its_transfers()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 500m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Date = new DateOnly(2026, 3, 1),
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = savings,
        });

        await book.Accounts.DeleteAsync(checking);

        // Leaving the savings leg behind would silently move that account's balance.
        await using MyFinanceDbContext db = book.CreateContext();
        db.Transactions.Count().ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_an_account_that_is_gone_is_reported_cleanly()
    {
        using var book = new BookHarness();

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Accounts.DeleteAsync(9_999));

        thrown.Errors.ShouldContain(e => e.Code == AccountService.NotFound);
    }

    [Fact]
    public async Task The_account_list_groups_and_subtotals_the_way_the_banking_screen_shows_it()
    {
        using var book = new BookHarness();

        int checking = await book.AddAccountAsync("Everyday Checking", AccountType.Checking);
        int savings = await book.AddAccountAsync("Holiday Fund", AccountType.Savings);
        int card = await book.AddAccountAsync("Rewards Card", AccountType.CreditCard);

        await book.AddTransactionAsync(checking, -1_240.50m);
        await book.AddTransactionAsync(savings, 2_500.16m);
        await book.AddTransactionAsync(card, -410.25m);

        AccountListSummary list = await book.Accounts.GetAccountListAsync();

        list.Groups.Count.ShouldBe(2);
        list.Groups[0].Group.ShouldBe(AccountGroup.Bank);
        list.Groups[0].Header.ShouldBe("Bank accounts");
        list.Groups[0].Subtotal.ShouldBe(Money.FromDecimal(1_259.66m));

        list.Groups[1].Group.ShouldBe(AccountGroup.Credit);
        list.Groups[1].Subtotal.ShouldBe(Money.FromDecimal(-410.25m));

        // A liability's balance is already negative, so net worth is a straight sum.
        list.Total.ShouldBe(Money.FromDecimal(849.41m));
    }

    [Fact]
    public async Task The_account_list_counts_the_opening_balance()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        await book.AddTransactionAsync(id, -250m);

        AccountListSummary list = await book.Accounts.GetAccountListAsync();

        list.Total.ShouldBe(Money.FromDecimal(750m));
    }

    [Fact]
    public async Task The_account_list_separates_the_bank_balance_from_the_current_balance()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        await book.AddTransactionAsync(id, -100m, cleared: ClearedStatus.Cleared);
        await book.AddTransactionAsync(id, -40m, cleared: ClearedStatus.Reconciled);
        await book.AddTransactionAsync(id, -7m, cleared: ClearedStatus.Uncleared);

        AccountSummary summary = (await book.Accounts.GetAccountListAsync()).AllAccounts.Single();

        summary.CurrentBalance.ShouldBe(Money.FromDecimal(853m));
        summary.ClearedBalance.ShouldBe(Money.FromDecimal(860m));
        summary.HasPendingItems.ShouldBeTrue();
        summary.TransactionCount.ShouldBe(3);
    }

    [Fact]
    public async Task Void_transactions_are_left_out_of_every_balance()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Checking", openingBalance: 100m);
        int transactionId = await book.AddTransactionAsync(id, -30m);

        await book.Register.SetVoidAsync(transactionId, isVoid: true);

        AccountSummary summary = (await book.Accounts.GetAccountListAsync()).AllAccounts.Single();
        summary.CurrentBalance.ShouldBe(Money.FromDecimal(100m));
    }

    [Fact]
    public async Task The_account_list_reports_how_much_still_needs_a_category()
    {
        using var book = new BookHarness();
        int id = await book.AddAccountAsync("Checking");
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.AddTransactionAsync(id, -30m, categoryId: groceries);
        await book.AddTransactionAsync(id, -12m);
        await book.AddTransactionAsync(id, -8m);

        AccountListSummary list = await book.Accounts.GetAccountListAsync();

        list.UncategorizedCount.ShouldBe(2);
    }

    [Fact]
    public async Task An_empty_book_produces_an_empty_list_rather_than_throwing()
    {
        using var book = new BookHarness();

        AccountListSummary list = await book.Accounts.GetAccountListAsync();

        list.Groups.ShouldBeEmpty();
        list.Total.ShouldBe(Money.Zero);
        list.AccountCount.ShouldBe(0);
    }

    [Fact]
    public async Task Accounts_can_be_reordered()
    {
        using var book = new BookHarness();
        int first = await book.AddAccountAsync("Alpha");
        int second = await book.AddAccountAsync("Beta");
        int third = await book.AddAccountAsync("Gamma");

        await book.Accounts.ReorderAsync([third, first, second]);

        IReadOnlyList<Account> ordered = await book.Accounts.GetAllAsync();
        ordered.Select(a => a.Id).ShouldBe([third, first, second]);
    }
}
