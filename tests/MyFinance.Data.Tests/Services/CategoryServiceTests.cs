using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

public sealed class CategoryServiceTests
{
    [Fact]
    public async Task A_new_book_starts_with_a_usable_chart_of_categories()
    {
        using var book = new BookHarness();

        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();

        categories.ShouldNotBeEmpty();
        categories.ShouldContain(c => c.FullName == "Bills : Mobile phone");
        categories.ShouldContain(c => c.FullName == "Income : Salary");
        categories.Single(c => c.FullName == "Income : Salary").Kind.ShouldBe(CategoryKind.Income);
        categories.Single(c => c.FullName == "Food : Groceries").Kind.ShouldBe(CategoryKind.Expense);
    }

    [Fact]
    public async Task Seeding_does_not_run_twice()
    {
        using var book = new BookHarness();

        await using MyFinanceDbContext db = book.CreateContext();
        int before = await db.Categories.CountAsync();

        int created = await DefaultCategories.SeedAsync(db);

        created.ShouldBe(0);
        (await db.Categories.CountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task A_category_can_be_created_under_a_heading()
    {
        using var book = new BookHarness();
        int bills = await book.CategoryIdAsync("Bills");

        int id = await book.Categories.CreateAsync("Window cleaner", bills, CategoryKind.Expense);

        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();
        categories.Single(c => c.Id == id).FullName.ShouldBe("Bills : Window cleaner");
    }

    [Fact]
    public async Task A_child_takes_its_parents_kind()
    {
        using var book = new BookHarness();
        int income = await book.CategoryIdAsync("Income");

        // Passed as an expense on purpose: a subcategory of Income that reports as spending
        // would split one heading across both halves of every income-versus-spending report.
        int id = await book.Categories.CreateAsync("Royalties", income, CategoryKind.Expense);

        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();
        categories.Single(c => c.Id == id).Kind.ShouldBe(CategoryKind.Income);
    }

    [Fact]
    public async Task Categories_stop_at_two_levels()
    {
        using var book = new BookHarness();
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Categories.CreateAsync("Organic", groceries, CategoryKind.Expense));

        thrown.Errors.ShouldContain(e => e.Code == CategoryService.ParentDepthExceeded);
    }

    [Fact]
    public async Task The_same_name_can_appear_under_two_different_headings()
    {
        using var book = new BookHarness();

        // "Leisure : Travel" and "Business : Travel" both ship in the defaults.
        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();

        categories.ShouldContain(c => c.FullName == "Leisure : Travel");
        categories.ShouldContain(c => c.FullName == "Business : Travel");
    }

    [Fact]
    public async Task Two_categories_under_one_heading_cannot_share_a_name()
    {
        using var book = new BookHarness();
        int food = await book.CategoryIdAsync("Food");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Categories.CreateAsync("Groceries", food, CategoryKind.Expense));

        thrown.Errors.ShouldContain(e => e.Code == CategoryService.NameDuplicate);
    }

    [Fact]
    public async Task A_category_can_be_renamed()
    {
        using var book = new BookHarness();
        int id = await book.CategoryIdAsync("Food : Takeaway");

        await book.Categories.UpdateAsync(id, "Delivery", await book.CategoryIdAsync("Food"), CategoryKind.Expense, false);

        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();
        categories.Single(c => c.Id == id).FullName.ShouldBe("Food : Delivery");
    }

    [Fact]
    public async Task Flipping_a_headings_kind_takes_its_children_with_it()
    {
        using var book = new BookHarness();
        int financial = await book.CategoryIdAsync("Financial");

        await book.Categories.UpdateAsync(financial, "Financial", null, CategoryKind.Income, false);

        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();
        categories
            .Where(c => c.FullName.StartsWith("Financial", StringComparison.Ordinal))
            .ShouldAllBe(c => c.Kind == CategoryKind.Income);
    }

    [Fact]
    public async Task An_unused_category_can_be_deleted()
    {
        using var book = new BookHarness();
        int id = await book.CategoryIdAsync("Charity : Donations");

        await book.Categories.DeleteAsync(id);

        (await book.Categories.GetAllAsync()).ShouldNotContain(c => c.Id == id);
    }

    [Fact]
    public async Task A_category_with_history_cannot_be_deleted()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.AddTransactionAsync(account, -30m, categoryId: groceries);

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Categories.DeleteAsync(groceries));

        // Deleting it would have to either destroy or re-file transactions that were
        // correctly categorized at the time.
        thrown.Errors.ShouldContain(e => e.Code == CategoryService.CategoryInUse);
    }

    [Fact]
    public async Task A_heading_with_subcategories_cannot_be_deleted()
    {
        using var book = new BookHarness();
        int food = await book.CategoryIdAsync("Food");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Categories.DeleteAsync(food));

        thrown.Errors.ShouldContain(e => e.Code == CategoryService.HasChildren);
    }

    [Fact]
    public async Task Archiving_hides_a_category_without_losing_its_history()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        int transactionId = await book.AddTransactionAsync(account, -4.50m, categoryId: coffee);

        await book.Categories.SetArchivedAsync(coffee, isArchived: true);

        (await book.Categories.GetAllAsync()).ShouldNotContain(c => c.Id == coffee);
        (await book.Categories.GetAllAsync(includeArchived: true)).ShouldContain(c => c.Id == coffee);

        Core.Entities.Transaction? saved = await book.Register.FindAsync(transactionId);
        saved!.Splits.Single().CategoryId.ShouldBe(coffee);
    }

    [Fact]
    public async Task Archiving_a_heading_archives_its_children()
    {
        using var book = new BookHarness();
        int leisure = await book.CategoryIdAsync("Leisure");

        await book.Categories.SetArchivedAsync(leisure, isArchived: true);

        IReadOnlyList<CategoryListItem> visible = await book.Categories.GetAllAsync();
        visible.ShouldNotContain(c => c.FullName.StartsWith("Leisure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Merging_moves_the_transactions_and_retires_the_source()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int coffee = await book.CategoryIdAsync("Food : Coffee");
        int restaurants = await book.CategoryIdAsync("Food : Restaurants");

        int first = await book.AddTransactionAsync(account, -4.50m, categoryId: coffee);
        int second = await book.AddTransactionAsync(account, -6.00m, categoryId: coffee);

        await book.Categories.MergeAsync(coffee, restaurants);

        (await book.Categories.GetAllAsync(includeArchived: true)).ShouldNotContain(c => c.Id == coffee);

        (await book.Register.FindAsync(first))!.Splits.Single().CategoryId.ShouldBe(restaurants);
        (await book.Register.FindAsync(second))!.Splits.Single().CategoryId.ShouldBe(restaurants);
    }

    [Fact]
    public async Task The_category_list_reports_how_often_each_one_is_used()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int groceries = await book.CategoryIdAsync("Food : Groceries");

        await book.AddTransactionAsync(account, -30m, categoryId: groceries);
        await book.AddTransactionAsync(account, -40m, categoryId: groceries);

        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();

        categories.Single(c => c.Id == groceries).UseCount.ShouldBe(2);
        categories.Single(c => c.FullName == "Food : Coffee").UseCount.ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_category_a_payee_remembers_does_not_fail()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int donations = await book.CategoryIdAsync("Charity : Donations");

        int id = await book.AddTransactionAsync(account, -25m, payee: "Red Cross", categoryId: donations);
        await book.Register.DeleteAsync(id);

        // The payee still points at the category it last used; the delete has to release
        // that reference rather than trip over the foreign key.
        await book.Categories.DeleteAsync(donations);

        PayeeListItem? payee = await book.Payees.FindByNameAsync("Red Cross");
        payee.ShouldNotBeNull();
        payee.LastCategoryId.ShouldBeNull();
    }

    [Fact]
    public async Task Only_headings_are_offered_as_parents()
    {
        using var book = new BookHarness();

        IReadOnlyList<Core.Entities.Category> parents = await book.Categories.GetParentsAsync();

        parents.ShouldNotBeEmpty();
        parents.ShouldAllBe(c => c.ParentId == null);
        parents.ShouldContain(c => c.Name == "Bills");
    }
}
