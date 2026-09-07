using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Payees;
using MyFinance.Core.Primitives;

namespace MyFinance.Data.Services;

/// <summary>A payee with what the register needs to pre-fill and what the list shows.</summary>
public sealed record PayeeListItem
{
    public required Payee Payee { get; init; }

    public required int UseCount { get; init; }

    /// <summary>Display path of the payee's remembered category, if any.</summary>
    public string? LastCategoryName { get; init; }

    public int Id => Payee.Id;

    public string Name => Payee.Name;

    public int? LastCategoryId => Payee.LastCategoryId;

    public Money? LastAmount => Payee.LastAmount;
}

/// <summary>Looks up, creates, renames, merges and deletes payees.</summary>
public sealed class PayeeService
{
    public const string NameRequired = "payee.name_required";
    public const string NameDuplicate = "payee.name_duplicate";
    public const string NotFound = "payee.not_found";
    public const string PayeeInUse = "payee.in_use";

    private readonly IBookContextFactory _factory;

    public PayeeService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<IReadOnlyList<PayeeListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<Payee> payees = await db.Payees
            .AsNoTracking()
            .Include(p => p.LastCategory)
            .ThenInclude(c => c!.Parent)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, int> useCounts = await db.Transactions
            .AsNoTracking()
            .Where(t => t.PayeeId != null)
            .GroupBy(t => t.PayeeId!.Value)
            .Select(g => new { PayeeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PayeeId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. payees.Select(p => new PayeeListItem
            {
                Payee = p,
                UseCount = useCounts.GetValueOrDefault(p.Id),
                LastCategoryName = p.LastCategory?.FullName,
            })
        ];
    }

    /// <summary>Names only, for the register's payee autocomplete.</summary>
    public async Task<IReadOnlyList<string>> GetNamesAsync(CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.Payees
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What the register should pre-fill after the user picks a payee: the category and
    /// amount last used with them.
    /// </summary>
    public async Task<PayeeListItem?> FindByNameAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        string normalized = PayeeNormalizer.Normalize(name);
        if (normalized.Length == 0)
        {
            return null;
        }

        await using MyFinanceDbContext db = _factory.CreateContext();

        Payee? payee = await db.Payees
            .AsNoTracking()
            .Include(p => p.LastCategory)
            .ThenInclude(c => c!.Parent)
            .FirstOrDefaultAsync(p => p.NormalizedName == normalized, cancellationToken)
            .ConfigureAwait(false);

        if (payee is null)
        {
            return null;
        }

        int uses = await db.Transactions
            .CountAsync(t => t.PayeeId == payee.Id, cancellationToken)
            .ConfigureAwait(false);

        return new PayeeListItem
        {
            Payee = payee,
            UseCount = uses,
            LastCategoryName = payee.LastCategory?.FullName,
        };
    }

    /// <summary>
    /// Returns the id of the payee with this name, creating it if there is none. Matching is
    /// on the normalized name, so "Blue Bottle" and "BLUE BOTTLE." are the same payee.
    /// </summary>
    public async Task<int> FindOrCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new BookValidationException(NameRequired, "A payee needs a name.");
        }

        Payee payee = await ResolveAsync(db, trimmed, cancellationToken).ConfigureAwait(false)
            ?? throw new BookValidationException(NameRequired, "A payee needs a name.");

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return payee.Id;
    }

    public async Task RenameAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        Payee payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That payee no longer exists.");

        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new BookValidationException(NameRequired, "A payee needs a name.");
        }

        bool taken = await db.Payees
            .AnyAsync(p => p.Name == trimmed && p.Id != id, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            throw new BookValidationException(
                NameDuplicate,
                $"A payee called \"{trimmed}\" already exists. Merge them instead.");
        }

        payee.Name = trimmed;
        payee.NormalizedName = PayeeNormalizer.Normalize(trimmed);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a payee that nothing references.</summary>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        Payee payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That payee no longer exists.");

        int uses = await db.Transactions
            .CountAsync(t => t.PayeeId == id, cancellationToken)
            .ConfigureAwait(false);

        if (uses > 0)
        {
            throw new BookValidationException(
                PayeeInUse,
                $"\"{payee.Name}\" is used by {uses} transaction{(uses == 1 ? string.Empty : "s")}. Merge it into another payee instead.");
        }

        db.Payees.Remove(payee);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-points every transaction from one payee to another and deletes the source, keeping
    /// the source's descriptors as aliases so a future import of the same text lands on the
    /// surviving payee rather than recreating the duplicate.
    /// </summary>
    public async Task MergeAsync(int sourceId, int targetId, CancellationToken cancellationToken = default)
    {
        if (sourceId == targetId)
        {
            throw new BookValidationException(NotFound, "Pick a different payee to merge into.");
        }

        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Payee source = await db.Payees
            .Include(p => p.Aliases)
            .FirstOrDefaultAsync(p => p.Id == sourceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That payee no longer exists.");

        Payee target = await db.Payees
            .Include(p => p.Aliases)
            .FirstOrDefaultAsync(p => p.Id == targetId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That payee no longer exists.");

        List<Transaction> transactions = await db.Transactions
            .Where(t => t.PayeeId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Transaction entry in transactions)
        {
            entry.PayeeId = targetId;
        }

        var existingAliases = target.Aliases
            .Select(a => a.NormalizedPattern)
            .ToHashSet(StringComparer.Ordinal);

        foreach (PayeeAlias alias in source.Aliases.ToList())
        {
            if (existingAliases.Add(alias.NormalizedPattern))
            {
                alias.PayeeId = targetId;
            }
            else
            {
                db.PayeeAliases.Remove(alias);
            }
        }

        if (existingAliases.Add(source.NormalizedName))
        {
            db.PayeeAliases.Add(new PayeeAlias
            {
                PayeeId = targetId,
                NormalizedPattern = source.NormalizedName,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.Payees.Remove(source);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds an existing payee for a typed name, or stages a new one on the context.
    /// </summary>
    /// <remarks>
    /// Internal because the register calls it inside its own transaction: creating the payee
    /// and the transaction that names it has to be one atomic write, or a failed save leaves
    /// behind a payee for a transaction that was never recorded. Returns null for blank
    /// input, which is legitimate — a cash withdrawal has no payee.
    /// </remarks>
    internal static async Task<Payee?> ResolveAsync(
        MyFinanceDbContext db,
        string? name,
        CancellationToken cancellationToken)
    {
        string trimmed = (name ?? string.Empty).Trim();
        string normalized = PayeeNormalizer.Normalize(trimmed);

        if (normalized.Length == 0)
        {
            return null;
        }

        Payee? existing = await db.Payees
            .FirstOrDefaultAsync(p => p.NormalizedName == normalized, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        // Also honours anything already staged in this context but not yet saved, so two
        // rows naming the same new payee in one operation share it.
        Payee? staged = db.ChangeTracker
            .Entries<Payee>()
            .Select(e => e.Entity)
            .FirstOrDefault(p => string.Equals(p.NormalizedName, normalized, StringComparison.Ordinal));

        if (staged is not null)
        {
            return staged;
        }

        var payee = new Payee
        {
            Name = trimmed,
            NormalizedName = normalized,
        };

        db.Payees.Add(payee);
        return payee;
    }
}
