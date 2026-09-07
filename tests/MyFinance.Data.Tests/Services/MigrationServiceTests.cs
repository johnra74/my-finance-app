using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Import.Model;
using MyFinance.Import.Mny;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// Migration tested against books built in code rather than against a real Money file.
/// </summary>
/// <remarks>
/// A `.mny` is somebody's whole financial history and is never committed, so the mapping is
/// exercised with small books assembled here. That also lets each rule be tested on its own,
/// which reading one real file never could.
/// </remarks>
public sealed class MigrationServiceTests
{
    private static MoneyBook Book(
        IEnumerable<MoneyAccount>? accounts = null,
        IEnumerable<MoneyCategory>? categories = null,
        IEnumerable<MoneyPayee>? payees = null,
        IEnumerable<MoneyTransaction>? transactions = null) =>
        new()
        {
            Accounts = [.. accounts ?? []],
            Categories = [.. categories ?? []],
            Payees = [.. payees ?? []],
            Transactions = [.. transactions ?? []],
            Diagnostics = [],
        };

    private static MoneyAccount Account(
        int id,
        string name,
        AccountType type = AccountType.Checking,
        decimal opening = 0m,
        bool closed = false) =>
        new(id, name, type, AccountGroup.Bank, closed, false,
            Money.FromDecimal(opening), null, null, null, null);

    private static MoneyTransaction Transaction(
        int id,
        int account,
        string date,
        decimal amount,
        int? category = null,
        int? payee = null,
        bool transfer = false,
        bool source = false,
        int? linked = null,
        int? splitParent = null,
        int splitIndex = 0,
        bool scheduled = false) =>
        new(id, account, linked, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            Money.FromDecimal(amount), category, payee, null, null,
            ClearedStatus.Reconciled, transfer, source, splitParent, splitIndex, scheduled);

    [Fact]
    public async Task An_account_and_its_transactions_come_across()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Everyday Checking", opening: 100m)],
            transactions:
            [
                Transaction(10, 1, "2020-01-05", -40m),
                Transaction(11, 1, "2020-01-06", 250m),
            ]);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        summary.AccountsCreated.ShouldBe(1);
        summary.TransactionsCreated.ShouldBe(2);

        await using MyFinanceDbContext db = harness.CreateContext();
        Account account = await db.Accounts.SingleAsync();

        account.Name.ShouldBe("Everyday Checking");
        account.OpeningBalance.ShouldBe(Money.FromDecimal(100m));
        (await db.Transactions.CountAsync()).ShouldBe(2);
    }

    /// <summary>
    /// The whole point of the exercise: the migrated balance has to equal the one Money
    /// showed, or nobody can tell whether anything was lost on the way across.
    /// </summary>
    [Fact]
    public async Task The_migrated_balance_matches_the_balance_money_reported()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking", opening: 861.57m)],
            transactions:
            [
                Transaction(10, 1, "2020-01-05", -125.00m),
                Transaction(11, 1, "2020-02-05", -4623.55m),
                Transaction(12, 1, "2020-03-05", 9000.00m),
            ]);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        Money expected = book.BalanceOf(1);
        expected.ShouldBe(Money.FromDecimal(5113.02m));
        summary.Accounts.Single().Balance.ShouldBe(expected);

        await using MyFinanceDbContext db = harness.CreateContext();
        Account account = await db.Accounts.SingleAsync();
        Money actual = account.OpeningBalance
            + Money.Sum(await db.Transactions.Select(t => t.Amount).ToListAsync());

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task Money_three_level_categories_become_two_levels_plus_a_kind()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking")],
            categories:
            [
                new MoneyCategory(130, "INCOME", null, 0, CategoryKind.Income),
                new MoneyCategory(131, "EXPENSE", null, 0, CategoryKind.Expense),
                new MoneyCategory(140, "Bills", 131, 1, CategoryKind.Expense),
                new MoneyCategory(141, "Mobile phone", 140, 2, CategoryKind.Expense),
                new MoneyCategory(150, "Salary", 130, 1, CategoryKind.Income),
            ]);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        // The two roots are a flag here, not rows, so only three categories are written.
        summary.CategoriesCreated.ShouldBe(3);

        await using MyFinanceDbContext db = harness.CreateContext();
        List<Category> categories = await db.Categories.ToListAsync();

        categories.Count.ShouldBe(3);
        categories.ShouldNotContain(c => c.Name == "INCOME" || c.Name == "EXPENSE");

        Category bills = categories.Single(c => c.Name == "Bills");
        Category phone = categories.Single(c => c.Name == "Mobile phone");
        Category salary = categories.Single(c => c.Name == "Salary");

        bills.ParentId.ShouldBeNull();
        bills.Kind.ShouldBe(CategoryKind.Expense);
        phone.ParentId.ShouldBe(bills.Id);
        salary.Kind.ShouldBe(CategoryKind.Income);
        salary.ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task A_transaction_keeps_its_category()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking")],
            categories:
            [
                new MoneyCategory(131, "EXPENSE", null, 0, CategoryKind.Expense),
                new MoneyCategory(140, "Bills", 131, 1, CategoryKind.Expense),
            ],
            transactions: [Transaction(10, 1, "2020-01-05", -40m, category: 140)]);

        await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();
        TransactionSplit split = await db.TransactionSplits.SingleAsync();
        Category bills = await db.Categories.SingleAsync(c => c.Name == "Bills");

        split.CategoryId.ShouldBe(bills.Id);
        split.Amount.ShouldBe(Money.FromDecimal(-40m));
    }

    /// <summary>Every transaction carries a split, so the register has one shape to read.</summary>
    [Fact]
    public async Task An_uncategorized_transaction_still_gets_one_split()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking")],
            transactions: [Transaction(10, 1, "2020-01-05", -40m)]);

        await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();
        TransactionSplit split = await db.TransactionSplits.SingleAsync();

        split.CategoryId.ShouldBeNull();
        split.Amount.ShouldBe(Money.FromDecimal(-40m));
    }

    [Fact]
    public async Task A_split_transaction_becomes_one_transaction_with_several_splits()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking")],
            transactions:
            [
                Transaction(10, 1, "2020-01-05", -340m),
                Transaction(11, 1, "2020-01-05", -280m, splitParent: 10, splitIndex: 0),
                Transaction(12, 1, "2020-01-05", -60m, splitParent: 10, splitIndex: 1),
            ]);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        summary.TransactionsCreated.ShouldBe(1);
        summary.SplitsCreated.ShouldBe(2);

        await using MyFinanceDbContext db = harness.CreateContext();
        Transaction transaction = await db.Transactions.Include(t => t.Splits).SingleAsync();

        transaction.Amount.ShouldBe(Money.FromDecimal(-340m));
        transaction.Splits.Count.ShouldBe(2);
        Money.Sum(transaction.Splits.Select(s => s.Amount)).ShouldBe(transaction.Amount);
    }

    /// <summary>
    /// Money writes a transfer as two rows that cancel, exactly as this application does, so
    /// the pair has to end up pointing at one another rather than as two loose entries.
    /// </summary>
    [Fact]
    public async Task The_two_legs_of_a_transfer_are_linked_to_each_other()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking"), Account(2, "Savings")],
            transactions:
            [
                Transaction(10, 1, "2020-01-05", -500m, transfer: true, source: true, linked: 2),
                Transaction(11, 2, "2020-01-05", 500m, transfer: true, linked: 1),
            ]);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        summary.TransfersLinked.ShouldBe(1);

        await using MyFinanceDbContext db = harness.CreateContext();
        List<Transaction> transactions = await db.Transactions.OrderBy(t => t.Id).ToListAsync();

        transactions.Count.ShouldBe(2);
        transactions[0].TransferPeerId.ShouldBe(transactions[1].Id);
        transactions[1].TransferPeerId.ShouldBe(transactions[0].Id);
    }

    [Fact]
    public async Task Payees_come_across_and_transactions_still_point_at_them()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking")],
            payees: [new MoneyPayee(500, "Blue Bottle Coffee", false)],
            transactions: [Transaction(10, 1, "2020-01-05", -6m, payee: 500)]);

        await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();
        Payee payee = await db.Payees.SingleAsync();
        Transaction transaction = await db.Transactions.SingleAsync();

        payee.Name.ShouldBe("Blue Bottle Coffee");
        transaction.PayeeId.ShouldBe(payee.Id);
    }

    /// <summary>
    /// Twenty-five years of a book accumulates payees that differ only by punctuation.
    /// Folding them keeps the payee list usable, and no transaction may lose its payee.
    /// </summary>
    [Fact]
    public async Task Payees_that_normalize_alike_are_folded_together()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking")],
            payees:
            [
                new MoneyPayee(500, "Blue Bottle Coffee", false),
                new MoneyPayee(501, "BLUE BOTTLE COFFEE", false),
            ],
            transactions:
            [
                Transaction(10, 1, "2020-01-05", -6m, payee: 500),
                Transaction(11, 1, "2020-01-06", -7m, payee: 501),
            ]);

        await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();
        Payee payee = await db.Payees.SingleAsync();

        (await db.Transactions.CountAsync(t => t.PayeeId == payee.Id)).ShouldBe(2);
    }

    /// <summary>
    /// What makes a migrated book useful straight away: the categories the user chose over
    /// twenty-five years become the memory that fills in the next import.
    /// </summary>
    [Fact]
    public async Task A_payee_remembers_the_category_it_was_last_filed_under()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking")],
            categories:
            [
                new MoneyCategory(131, "EXPENSE", null, 0, CategoryKind.Expense),
                new MoneyCategory(140, "Groceries", 131, 1, CategoryKind.Expense),
                new MoneyCategory(141, "Coffee", 131, 1, CategoryKind.Expense),
            ],
            payees: [new MoneyPayee(500, "Blue Bottle", false)],
            transactions:
            [
                Transaction(10, 1, "2019-01-05", -6m, category: 140, payee: 500),
                Transaction(11, 1, "2020-06-05", -7m, category: 141, payee: 500),
            ]);

        await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();
        Payee payee = await db.Payees.SingleAsync();
        Category coffee = await db.Categories.SingleAsync(c => c.Name == "Coffee");

        payee.LastCategoryId.ShouldBe(coffee.Id);
    }

    [Fact]
    public async Task Closed_accounts_come_across_by_default()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(accounts:
        [
            Account(1, "Checking"),
            Account(2, "Old Card", AccountType.CreditCard, closed: true),
        ]);

        await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();
        (await db.Accounts.CountAsync()).ShouldBe(2);
        (await db.Accounts.SingleAsync(a => a.Name == "Old Card")).IsClosed.ShouldBeTrue();
    }

    [Fact]
    public async Task Closed_accounts_can_be_left_behind_on_request()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts:
            [
                Account(1, "Checking"),
                Account(2, "Old Card", AccountType.CreditCard, closed: true),
            ],
            transactions: [Transaction(10, 2, "2005-01-05", -40m)]);

        MigrationSummary summary = await harness.Migration.MigrateAsync(
            book,
            new MigrationOptions { IncludeClosedAccounts = false });

        summary.AccountsCreated.ShouldBe(1);

        // The orphaned transaction is reported rather than dropped in silence.
        summary.Diagnostics.ShouldContain(d => d.Code == "migration.orphan_transactions");
    }

    /// <summary>
    /// Money counts the bills it projected into the register, so they are kept by default —
    /// otherwise the migrated balance would not match the figure the user is checking against.
    /// </summary>
    [Fact]
    public async Task Projected_bills_are_kept_by_default_and_can_be_left_out()
    {
        MoneyBook book = Book(
            accounts: [Account(1, "Checking", opening: 1000m)],
            transactions:
            [
                Transaction(10, 1, "2020-01-05", -100m),
                Transaction(11, 1, "2021-01-05", -250m, scheduled: true),
            ]);

        using (var kept = new BookHarness())
        {
            MigrationSummary summary = await kept.Migration.MigrateAsync(book);

            summary.TransactionsCreated.ShouldBe(2);
            summary.Accounts.Single().Balance.ShouldBe(Money.FromDecimal(650m));
        }

        using var dropped = new BookHarness();

        MigrationSummary trimmed = await dropped.Migration.MigrateAsync(
            book,
            new MigrationOptions { IncludeScheduledInstances = false });

        trimmed.TransactionsCreated.ShouldBe(1);
        trimmed.Accounts.Single().Balance.ShouldBe(Money.FromDecimal(900m));
    }

    /// <summary>
    /// Merging a Money book into one that already has entries would make duplicates nobody
    /// could untangle, and there is no undo worth the name for forty thousand rows.
    /// </summary>
    [Fact]
    public async Task Migrating_into_a_book_that_already_has_entries_is_refused()
    {
        using var harness = new BookHarness();
        await harness.AddAccountAsync("Existing");

        MoneyBook book = Book(accounts: [Account(1, "Checking")]);

        BookValidationException error = await Should.ThrowAsync<BookValidationException>(
            () => harness.Migration.MigrateAsync(book));

        error.Errors.ShouldContain(e => e.Code == MigrationService.BookNotEmpty);

        await using MyFinanceDbContext db = harness.CreateContext();
        (await db.Accounts.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_file_with_no_accounts_is_refused_rather_than_half_written()
    {
        using var harness = new BookHarness();

        BookValidationException error = await Should.ThrowAsync<BookValidationException>(
            () => harness.Migration.MigrateAsync(Book()));

        error.Errors.ShouldContain(e => e.Code == MigrationService.NothingToMigrate);
    }

    [Fact]
    public async Task Transactions_on_the_same_day_keep_a_stable_order()
    {
        using var harness = new BookHarness();

        MoneyBook book = Book(
            accounts: [Account(1, "Checking")],
            transactions:
            [
                Transaction(10, 1, "2020-01-05", -10m),
                Transaction(11, 1, "2020-01-05", -20m),
                Transaction(12, 1, "2020-01-05", -30m),
            ]);

        await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();
        List<int> sequences = await db.Transactions
            .OrderBy(t => t.SequenceInDay)
            .Select(t => t.SequenceInDay)
            .ToListAsync();

        sequences.ShouldBe([0, 1, 2]);
    }
}
