using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Validation;

namespace MyFinance.Data.Services;

/// <summary>A category with its display path and usage count, for the categories screen.</summary>
public sealed record CategoryListItem
{
    public required Category Category { get; init; }

    /// <summary>"Bills : Mobile phone" for a child, "Bills" for a parent.</summary>
    public required string FullName { get; init; }

    /// <summary>Splits pointing at this category. Non-zero blocks deletion.</summary>
    public required int UseCount { get; init; }

    public int Id => Category.Id;

    public bool IsParent => Category.ParentId is null;

    public CategoryKind Kind => Category.Kind;

    public bool IsArchived => Category.IsArchived;
}

/// <summary>Creates, renames, archives and deletes categories.</summary>
public sealed class CategoryService
{
    public const string NameRequired = "category.name_required";
    public const string NameDuplicate = "category.name_duplicate";
    public const string NotFound = "category.not_found";
    public const string ParentDepthExceeded = "category.depth_exceeded";
    public const string CategoryInUse = "category.in_use";
    public const string HasChildren = "category.has_children";

    private readonly IBookContextFactory _factory;

    public CategoryService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// Every category, ordered as a tree: each parent followed by its children.
    /// </summary>
    public async Task<IReadOnlyList<CategoryListItem>> GetAllAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<Category> categories = await db.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
            .Where(c => includeArchived || !c.IsArchived)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, int> useCounts = await db.TransactionSplits
            .AsNoTracking()
            .Where(s => s.CategoryId != null)
            .GroupBy(s => s.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        // Sorted by the rendered path so the flat list reads as a tree without the view
        // having to nest anything: "Bills", "Bills : Mobile phone", "Food", ...
        return
        [
            .. categories
                .Select(c => new CategoryListItem
                {
                    Category = c,
                    FullName = c.FullName,
                    UseCount = useCounts.GetValueOrDefault(c.Id),
                })
                .OrderBy(c => c.FullName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>Top-level categories only, for the parent picker in the category editor.</summary>
    public async Task<IReadOnlyList<Category>> GetParentsAsync(CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.Categories
            .AsNoTracking()
            .Where(c => c.ParentId == null && !c.IsArchived)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(
        string name,
        int? parentId,
        CategoryKind kind,
        bool isTaxRelated = false,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        string trimmed = (name ?? string.Empty).Trim();
        await GuardAsync(db, trimmed, parentId, excludingId: null, cancellationToken).ConfigureAwait(false);

        // A child inherits its parent's kind: "Bills : Mobile phone" cannot be income while
        // "Bills" is an expense, and letting the two disagree would split one heading across
        // both halves of every income-versus-spending report.
        if (parentId is int parent)
        {
            Category parentCategory = await RequireAsync(db, parent, cancellationToken).ConfigureAwait(false);
            kind = parentCategory.Kind;
        }

        var category = new Category
        {
            Name = trimmed,
            ParentId = parentId,
            Kind = kind,
            IsTaxRelated = isTaxRelated,
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return category.Id;
    }

    public async Task UpdateAsync(
        int id,
        string name,
        int? parentId,
        CategoryKind kind,
        bool isTaxRelated,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        Category category = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        string trimmed = (name ?? string.Empty).Trim();
        await GuardAsync(db, trimmed, parentId, excludingId: id, cancellationToken).ConfigureAwait(false);

        if (parentId == id)
        {
            throw new BookValidationException(ParentDepthExceeded, "A category cannot be its own parent.");
        }

        bool hasChildren = await db.Categories
            .AnyAsync(c => c.ParentId == id, cancellationToken)
            .ConfigureAwait(false);

        if (hasChildren && parentId is not null)
        {
            throw new BookValidationException(
                ParentDepthExceeded,
                $"\"{category.Name}\" has subcategories, so it cannot become a subcategory itself. Categories are two levels deep.");
        }

        category.Name = trimmed;
        category.ParentId = parentId;
        category.IsTaxRelated = isTaxRelated;

        if (parentId is int parent)
        {
            Category parentCategory = await RequireAsync(db, parent, cancellationToken).ConfigureAwait(false);
            category.Kind = parentCategory.Kind;
        }
        else
        {
            category.Kind = kind;

            // Children follow the parent, so flipping a heading from expense to income moves
            // the whole heading rather than leaving its subcategories on the other side.
            List<Category> children = await db.Categories
                .Where(c => c.ParentId == id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (Category child in children)
            {
                child.Kind = kind;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hides a category from pickers while leaving history intact.
    /// </summary>
    /// <remarks>
    /// This is the answer for a category that is no longer used but has been used: deleting
    /// it would have to either destroy or re-file transactions that were correctly
    /// categorized at the time.
    /// </remarks>
    public async Task SetArchivedAsync(int id, bool isArchived, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        Category category = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        category.IsArchived = isArchived;

        List<Category> children = await db.Categories
            .Where(c => c.ParentId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Category child in children)
        {
            child.IsArchived = isArchived;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes an unused category. Refuses when transactions or subcategories still point at
    /// it, and says so — archiving is the right move for a category with history.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        Category category = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        bool hasChildren = await db.Categories
            .AnyAsync(c => c.ParentId == id, cancellationToken)
            .ConfigureAwait(false);

        if (hasChildren)
        {
            throw new BookValidationException(
                HasChildren,
                $"\"{category.Name}\" still has subcategories. Delete or move those first.");
        }

        int uses = await db.TransactionSplits
            .CountAsync(s => s.CategoryId == id, cancellationToken)
            .ConfigureAwait(false);

        if (uses > 0)
        {
            throw new BookValidationException(
                CategoryInUse,
                $"\"{category.Name}\" is used by {uses} transaction{(uses == 1 ? string.Empty : "s")}. Archive it instead to keep that history readable.");
        }

        // Payees remember their last category; clear the reference so the delete is not
        // blocked by a restricted foreign key.
        List<Payee> payees = await db.Payees
            .Where(p => p.LastCategoryId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Payee payee in payees)
        {
            payee.LastCategoryId = null;
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves every transaction from one category to another, then deletes the source. The
    /// way to retire a category that has been used without losing the transactions.
    /// </summary>
    public async Task MergeAsync(int sourceId, int targetId, CancellationToken cancellationToken = default)
    {
        if (sourceId == targetId)
        {
            throw new BookValidationException(NotFound, "Pick a different category to merge into.");
        }

        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Category source = await RequireAsync(db, sourceId, cancellationToken).ConfigureAwait(false);
        await RequireAsync(db, targetId, cancellationToken).ConfigureAwait(false);

        List<TransactionSplit> splits = await db.TransactionSplits
            .Where(s => s.CategoryId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (TransactionSplit split in splits)
        {
            split.CategoryId = targetId;
        }

        List<Payee> payees = await db.Payees
            .Where(p => p.LastCategoryId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Payee payee in payees)
        {
            payee.LastCategoryId = targetId;
        }

        List<Category> children = await db.Categories
            .Where(c => c.ParentId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Category child in children)
        {
            child.ParentId = null;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.Categories.Remove(source);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Category> RequireAsync(
        MyFinanceDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        Category? category = await db.Categories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return category ?? throw new BookValidationException(NotFound, "That category no longer exists.");
    }

    private static async Task GuardAsync(
        MyFinanceDbContext db,
        string name,
        int? parentId,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new ValidationError(NameRequired, "A category needs a name."));
        }
        else
        {
            bool taken = await db.Categories
                .AnyAsync(
                    c => c.Name == name
                        && c.ParentId == parentId
                        && (excludingId == null || c.Id != excludingId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (taken)
            {
                errors.Add(new ValidationError(
                    NameDuplicate,
                    $"There is already a category called \"{name}\" in that place."));
            }
        }

        // Two levels, as Microsoft Money has: a subcategory cannot itself have a parent.
        if (parentId is int parent)
        {
            int? grandparent = await db.Categories
                .Where(c => c.Id == parent)
                .Select(c => c.ParentId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (grandparent is not null)
            {
                errors.Add(new ValidationError(
                    ParentDepthExceeded,
                    "Categories go two levels deep, so a subcategory cannot be nested under another subcategory."));
            }
        }

        if (errors.Count > 0)
        {
            throw new BookValidationException(new ValidationResult(errors));
        }
    }
}
