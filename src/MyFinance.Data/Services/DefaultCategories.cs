using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;

namespace MyFinance.Data.Services;

/// <summary>
/// The starting category tree applied to a new book.
/// </summary>
/// <remarks>
/// A book with no categories is a book where every report is empty and every transaction
/// needs triage, so the very first thing a new user would have to do is invent a chart of
/// accounts. This is the same two-level shape Microsoft Money ships with, trimmed to
/// headings people actually use; everything here can be renamed, archived or deleted.
/// </remarks>
public static class DefaultCategories
{
    /// <summary>A heading and its subcategories.</summary>
    /// <param name="Name">The heading, e.g. "Bills".</param>
    /// <param name="Kind">Whether the heading is money in or money out.</param>
    /// <param name="Children">Subcategory names; empty for a heading used on its own.</param>
    public sealed record Group(string Name, CategoryKind Kind, params string[] Children);

    public static IReadOnlyList<Group> Groups { get; } =
    [
        new Group("Income", CategoryKind.Income,
            "Salary", "Bonus", "Interest", "Dividends", "Refunds", "Gifts received", "Other income"),

        new Group("Bills", CategoryKind.Expense,
            "Rent", "Mortgage", "Electricity", "Gas", "Water", "Internet", "Mobile phone",
            "Television", "Insurance", "Subscriptions"),

        new Group("Food", CategoryKind.Expense,
            "Groceries", "Restaurants", "Coffee", "Takeaway"),

        new Group("Transport", CategoryKind.Expense,
            "Fuel", "Public transport", "Parking", "Tolls", "Car maintenance", "Car payment", "Taxi"),

        new Group("Home", CategoryKind.Expense,
            "Repairs", "Furnishings", "Garden", "Household supplies", "Cleaning"),

        new Group("Health", CategoryKind.Expense,
            "Doctor", "Dentist", "Pharmacy", "Health insurance", "Fitness"),

        new Group("Shopping", CategoryKind.Expense,
            "Clothing", "Electronics", "Books", "Gifts given", "Hobbies"),

        new Group("Leisure", CategoryKind.Expense,
            "Travel", "Hotels", "Entertainment", "Events", "Streaming"),

        new Group("Family", CategoryKind.Expense,
            "Childcare", "Education", "Pets", "Allowance"),

        new Group("Financial", CategoryKind.Expense,
            "Bank charges", "Interest paid", "Loan payment", "Investment", "Savings contribution"),

        new Group("Taxes", CategoryKind.Expense,
            "Income tax", "Property tax", "Sales tax", "Other taxes"),

        new Group("Charity", CategoryKind.Expense,
            "Donations"),

        new Group("Business", CategoryKind.Expense,
            "Office supplies", "Professional fees", "Travel", "Software"),

        new Group("Miscellaneous", CategoryKind.Expense),
    ];

    /// <summary>
    /// Adds the default tree to a book that has no categories yet. Does nothing to a book
    /// that already has some, so it is safe to call on every open.
    /// </summary>
    /// <returns>How many categories were created.</returns>
    public static async Task<int> SeedAsync(
        MyFinanceDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (await context.Categories.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        int created = 0;
        int order = 0;

        foreach (Group group in Groups)
        {
            var parent = new Category
            {
                Name = group.Name,
                Kind = group.Kind,
                SortOrder = order++,
            };

            context.Categories.Add(parent);
            created++;

            int childOrder = 0;
            foreach (string child in group.Children)
            {
                // Children inherit the heading's kind, so a heading never straddles both
                // sides of an income-versus-spending report.
                context.Categories.Add(new Category
                {
                    Name = child,
                    Kind = group.Kind,
                    Parent = parent,
                    SortOrder = childOrder++,
                });

                created++;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <summary>Synchronous form, for the book-creation path which is already blocking.</summary>
    public static int Seed(MyFinanceDbContext context) =>
        SeedAsync(context).GetAwaiter().GetResult();
}
