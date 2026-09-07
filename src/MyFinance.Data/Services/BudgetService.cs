using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Budgeting;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Reporting;

namespace MyFinance.Data.Services;

/// <summary>The editable shape of one budgeted amount.</summary>
public sealed class BudgetDraft
{
    public int? Id { get; set; }

    public required int CategoryId { get; set; }

    /// <summary>First day of the period. Normalised to the first of the month on save.</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>Budgeted figure. Stored positive whatever sign the caller supplies.</summary>
    public Money Amount { get; set; }

    public bool RollsOver { get; set; }

    public string? Notes { get; set; }
}

/// <summary>A watched category and how it is tracking this month.</summary>
public sealed record SpendingWatchItem
{
    public required int CategoryId { get; init; }

    public required string CategoryPath { get; init; }

    public required Money Spent { get; init; }

    /// <summary>The budget for the current period, when one is set.</summary>
    public Money? Budgeted { get; init; }

    public Money? Remaining => Budgeted is Money budget ? budget - Spent : null;

    public bool IsOverBudget => Remaining is Money remaining && remaining.IsNegative;

    public decimal Progress => Budgeted is Money budget && !budget.IsZero
        ? (decimal)Spent.MinorUnits / budget.MinorUnits
        : 0m;

    public decimal ProgressCapped => Math.Clamp(Progress, 0m, 1m);

    public bool HasBudget => Budgeted is not null;
}

/// <summary>
/// Sets budgets and reports them against what actually happened.
/// </summary>
/// <remarks>
/// Budgets are held per category per month. An annual figure is the twelve monthly ones
/// added up rather than a separate kind of row, so there is only ever one place a number
/// lives and no way for the two views to disagree.
/// </remarks>
public sealed class BudgetService
{
    public const string CategoryNotFound = "budget.category_not_found";
    public const string NotFound = "budget.not_found";
    public const string AmountRequired = "budget.amount_required";

    private readonly IBookContextFactory _factory;

    public BudgetService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Budget against actual for one month.</summary>
    public async Task<BudgetPeriodResult> GetMonthAsync(
        DateOnly month,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        (List<BudgetAllocation> plans, List<BudgetActual> actuals) =
            await LoadAsync(db, cancellationToken).ConfigureAwait(false);

        return BudgetCalculator.ForPeriod(plans, actuals, Normalize(month));
    }

    /// <summary>Budget against actual across a run of months.</summary>
    public async Task<IReadOnlyList<BudgetPeriodResult>> GetSeriesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        (List<BudgetAllocation> plans, List<BudgetActual> actuals) =
            await LoadAsync(db, cancellationToken).ConfigureAwait(false);

        return BudgetCalculator.Series(plans, actuals, Normalize(from), Normalize(to));
    }

    /// <summary>A whole year rolled into one line per category.</summary>
    public async Task<BudgetPeriodResult> GetYearAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        (List<BudgetAllocation> plans, List<BudgetActual> actuals) =
            await LoadAsync(db, cancellationToken).ConfigureAwait(false);

        return BudgetCalculator.ForYear(plans, actuals, year);
    }

    /// <summary>Creates or updates the budget for one category and month.</summary>
    public async Task<int> SetAsync(BudgetDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using MyFinanceDbContext db = _factory.CreateContext();

        bool categoryExists = await db.Categories
            .AnyAsync(c => c.Id == draft.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (!categoryExists)
        {
            throw new BookValidationException(CategoryNotFound, "That category no longer exists.");
        }

        DateOnly period = Normalize(draft.PeriodStart);

        BudgetLine? line = await db.BudgetLines
            .FirstOrDefaultAsync(
                b => b.CategoryId == draft.CategoryId
                    && b.PeriodStart == period
                    && b.PeriodType == BudgetPeriodType.Monthly,
                cancellationToken)
            .ConfigureAwait(false);

        if (line is null)
        {
            line = new BudgetLine
            {
                CategoryId = draft.CategoryId,
                PeriodStart = period,
                PeriodType = BudgetPeriodType.Monthly,
            };

            db.BudgetLines.Add(line);
        }

        // Always stored positive: a budget is a statement of intent, not a movement, and
        // holding it signed would make every comparison against actuals read backwards.
        line.Amount = draft.Amount.Abs();
        line.RollsOver = draft.RollsOver;
        line.Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return line.Id;
    }

    /// <summary>
    /// Copies one month's budget onto a run of later months.
    /// </summary>
    /// <remarks>
    /// The only practical way to set up a year: a budget is mostly the same figure repeated,
    /// and typing twelve of everything is how people give up on budgeting.
    /// </remarks>
    /// <returns>How many monthly figures were written.</returns>
    public async Task<int> CopyMonthAsync(
        DateOnly source,
        DateOnly firstTarget,
        int months,
        bool overwriteExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (months <= 0)
        {
            return 0;
        }

        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        DateOnly from = Normalize(source);

        List<BudgetLine> template = await db.BudgetLines
            .AsNoTracking()
            .Where(b => b.PeriodStart == from && b.PeriodType == BudgetPeriodType.Monthly)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (template.Count == 0)
        {
            return 0;
        }

        int written = 0;

        for (int offset = 0; offset < months; offset++)
        {
            DateOnly target = Normalize(firstTarget).AddMonths(offset);

            if (target == from)
            {
                continue;
            }

            List<BudgetLine> existing = await db.BudgetLines
                .Where(b => b.PeriodStart == target && b.PeriodType == BudgetPeriodType.Monthly)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (BudgetLine source_ in template)
            {
                BudgetLine? found = existing.FirstOrDefault(b => b.CategoryId == source_.CategoryId);

                if (found is not null && !overwriteExisting)
                {
                    continue;
                }

                if (found is null)
                {
                    found = new BudgetLine
                    {
                        CategoryId = source_.CategoryId,
                        PeriodStart = target,
                        PeriodType = BudgetPeriodType.Monthly,
                    };

                    db.BudgetLines.Add(found);
                }

                found.Amount = source_.Amount;
                found.RollsOver = source_.RollsOver;
                written++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return written;
    }

    /// <summary>Removes the budget for one category and month.</summary>
    public async Task ClearAsync(
        int categoryId,
        DateOnly month,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        DateOnly period = Normalize(month);

        BudgetLine? line = await db.BudgetLines
            .FirstOrDefaultAsync(
                b => b.CategoryId == categoryId
                    && b.PeriodStart == period
                    && b.PeriodType == BudgetPeriodType.Monthly,
                cancellationToken)
            .ConfigureAwait(false);

        if (line is null)
        {
            return;
        }

        db.BudgetLines.Remove(line);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Categories the user is watching, with how they are tracking this month.</summary>
    public async Task<IReadOnlyList<SpendingWatchItem>> GetWatchListAsync(
        DateOnly month,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<WatchedCategory> watched = await db.WatchedCategories
            .AsNoTracking()
            .Include(w => w.Category)
            .ThenInclude(c => c!.Parent)
            .OrderBy(w => w.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (watched.Count == 0)
        {
            return [];
        }

        DateOnly period = Normalize(month);
        DateOnly end = period.AddMonths(1).AddDays(-1);

        IReadOnlyList<ReportEntry> entries = await ReportService
            .LoadEntriesAsync(db, cancellationToken)
            .ConfigureAwait(false);

        var window = ReportFilter.Everything with { From = period, To = end };

        Dictionary<int, Money> spent = entries
            .Where(e => window.Matches(e) && e.CategoryId is not null)
            .GroupBy(e => e.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => Money.Sum(g.Select(e => e.Amount)).Abs());

        Dictionary<int, Money> budgets = await db.BudgetLines
            .AsNoTracking()
            .Where(b => b.PeriodStart == period && b.PeriodType == BudgetPeriodType.Monthly)
            .ToDictionaryAsync(b => b.CategoryId, b => b.Amount, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. watched.Select(w => new SpendingWatchItem
            {
                CategoryId = w.CategoryId,
                CategoryPath = w.Category?.FullName ?? "(deleted category)",
                Spent = spent.GetValueOrDefault(w.CategoryId),
                Budgeted = budgets.TryGetValue(w.CategoryId, out Money budget) ? budget : null,
            })
        ];
    }

    /// <summary>Adds a category to the watch list, or removes it if it is already there.</summary>
    public async Task<bool> ToggleWatchAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        WatchedCategory? existing = await db.WatchedCategories
            .FirstOrDefaultAsync(w => w.CategoryId == categoryId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            db.WatchedCategories.Remove(existing);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        int highest = await db.WatchedCategories
            .Select(w => (int?)w.SortOrder)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

        db.WatchedCategories.Add(new WatchedCategory
        {
            CategoryId = categoryId,
            SortOrder = highest + 1,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Loads the budget and the actuals it is measured against.
    /// </summary>
    /// <remarks>
    /// Actuals come through the same report pipeline the reports use, so a budget and a
    /// spending report can never disagree about what a month came to — transfers excluded
    /// from both, voids excluded from both, splits counted the same way in both.
    /// </remarks>
    private static async Task<(List<BudgetAllocation>, List<BudgetActual>)> LoadAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<BudgetLine> lines = await db.BudgetLines
            .AsNoTracking()
            .Include(b => b.Category)
            .ThenInclude(c => c!.Parent)
            .Where(b => b.PeriodType == BudgetPeriodType.Monthly)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<BudgetAllocation> plans =
        [
            .. lines
                .Where(b => b.Category is not null)
                .Select(b => new BudgetAllocation(
                    b.CategoryId,
                    b.Category!.FullName,
                    b.Category.Kind,
                    b.PeriodStart,
                    b.Amount,
                    b.RollsOver))
        ];

        IReadOnlyList<ReportEntry> entries = await ReportService
            .LoadEntriesAsync(db, cancellationToken)
            .ConfigureAwait(false);

        ReportFilter filter = ReportFilter.Everything;

        List<BudgetActual> actuals =
        [
            .. entries
                .Where(e => filter.Matches(e) && e.CategoryId is not null)
                .GroupBy(e => (e.CategoryId!.Value, Period: new DateOnly(e.Date.Year, e.Date.Month, 1)))
                .Select(g => new BudgetActual(
                    g.Key.Value,
                    g.Key.Period,
                    Money.Sum(g.Select(e => e.Amount))))
        ];

        return (plans, actuals);
    }

    private static DateOnly Normalize(DateOnly date) => new(date.Year, date.Month, 1);
}
