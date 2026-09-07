using System.Globalization;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Budgeting;

/// <summary>A budgeted amount for one category over one period, as the calculator sees it.</summary>
/// <param name="CategoryId">The category budgeted.</param>
/// <param name="CategoryPath">Display path, e.g. "Food : Groceries".</param>
/// <param name="Kind">Whether the category is spending or income.</param>
/// <param name="PeriodStart">First day of the budgeted period.</param>
/// <param name="Amount">Budgeted figure, always positive.</param>
/// <param name="RollsOver">Whether an unspent remainder carries into the next period.</param>
public sealed record BudgetAllocation(
    int CategoryId,
    string CategoryPath,
    CategoryKind Kind,
    DateOnly PeriodStart,
    Money Amount,
    bool RollsOver);

/// <summary>What actually happened in one category over one period.</summary>
/// <param name="CategoryId">The category.</param>
/// <param name="PeriodStart">First day of the period.</param>
/// <param name="Amount">Signed total — spending is negative.</param>
public sealed record BudgetActual(int CategoryId, DateOnly PeriodStart, Money Amount);

/// <summary>Budget against actual for one category over one period.</summary>
public sealed record BudgetLineResult
{
    public required int CategoryId { get; init; }

    public required string CategoryPath { get; init; }

    public required CategoryKind Kind { get; init; }

    public required DateOnly PeriodStart { get; init; }

    /// <summary>What was budgeted for this period alone, always positive.</summary>
    public required Money Budgeted { get; init; }

    /// <summary>Unspent remainder carried in from earlier periods. Zero unless it rolls over.</summary>
    public required Money RolloverIn { get; init; }

    /// <summary>What was actually spent or received, always positive.</summary>
    public required Money Actual { get; init; }

    public required bool RollsOver { get; init; }

    /// <summary>Everything there was to spend: this period's figure plus anything carried in.</summary>
    public Money Available => Budgeted + RolloverIn;

    /// <summary>What is left. Negative means the budget was exceeded.</summary>
    public Money Remaining => Available - Actual;

    /// <summary>What carries into the next period, or zero when it does not roll over.</summary>
    public Money RolloverOut => RollsOver && Remaining.IsPositive ? Remaining : Money.Zero;

    public bool IsOverBudget => Remaining.IsNegative;

    /// <summary>How much of the budget has been used, as a fraction. Can exceed one.</summary>
    public decimal Progress => Available.IsZero
        ? (Actual.IsZero ? 0 : 1)
        : (decimal)Actual.MinorUnits / Available.MinorUnits;

    /// <summary>Progress capped at one, for a bar that must not overflow its track.</summary>
    public decimal ProgressCapped => Math.Clamp(Progress, 0m, 1m);

    public string ProgressText => Progress.ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>The over-budget portion as a fraction, for a bar that shows the overrun.</summary>
    public decimal Overrun => Progress <= 1m ? 0m : Math.Min(Progress - 1m, 1m);
}

/// <summary>A whole budget period: every line, and what the period comes to.</summary>
public sealed record BudgetPeriodResult
{
    public required DateOnly PeriodStart { get; init; }

    public required string Label { get; init; }

    public required IReadOnlyList<BudgetLineResult> Lines { get; init; }

    public IEnumerable<BudgetLineResult> Spending => Lines.Where(l => l.Kind == CategoryKind.Expense);

    public IEnumerable<BudgetLineResult> Income => Lines.Where(l => l.Kind == CategoryKind.Income);

    public Money BudgetedSpending => Money.Sum(Spending.Select(l => l.Available));

    public Money ActualSpending => Money.Sum(Spending.Select(l => l.Actual));

    public Money BudgetedIncome => Money.Sum(Income.Select(l => l.Available));

    public Money ActualIncome => Money.Sum(Income.Select(l => l.Actual));

    /// <summary>Budgeted income less budgeted spending — what the plan says should be left.</summary>
    public Money PlannedNet => BudgetedIncome - BudgetedSpending;

    /// <summary>What is actually left.</summary>
    public Money ActualNet => ActualIncome - ActualSpending;

    public Money RemainingToSpend => BudgetedSpending - ActualSpending;

    public int OverBudgetCount => Spending.Count(l => l.IsOverBudget);

    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>
/// Works out budget against actual, carrying unspent money forward where asked.
/// </summary>
/// <remarks>
/// <para>
/// Everything here works in positive figures. A budget is a statement of intent — "eighty a
/// month on coffee" — and negating it to match the register's sign convention would make
/// every comparison read backwards. The signed amounts are converted once, on the way in.
/// </para>
/// <para>
/// Rollover has to be computed period by period from the first budgeted one, because each
/// period's carried-in figure depends on the whole chain before it. There is no way to
/// answer "how much is left in March" without having answered it for January and February.
/// </para>
/// </remarks>
public static class BudgetCalculator
{
    /// <summary>
    /// Builds one period's budget lines.
    /// </summary>
    /// <param name="allocations">Every budgeted amount, across all periods.</param>
    /// <param name="actuals">What actually happened, across all periods.</param>
    /// <param name="periodStart">The period to report on.</param>
    public static BudgetPeriodResult ForPeriod(
        IEnumerable<BudgetAllocation> allocations,
        IEnumerable<BudgetActual> actuals,
        DateOnly periodStart)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(actuals);

        IReadOnlyList<BudgetPeriodResult> series = Series(allocations, actuals, periodStart, periodStart);

        return series.Count > 0
            ? series[0]
            : new BudgetPeriodResult
            {
                PeriodStart = periodStart,
                Label = periodStart.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
                Lines = [],
            };
    }

    /// <summary>
    /// Builds a run of consecutive monthly periods, carrying rollover through them.
    /// </summary>
    /// <remarks>
    /// The chain always starts at the earliest budgeted period rather than at
    /// <paramref name="from"/>, because a carried-in figure is the accumulated remainder of
    /// everything before it. Starting mid-way would silently report a rollover of zero.
    /// </remarks>
    public static IReadOnlyList<BudgetPeriodResult> Series(
        IEnumerable<BudgetAllocation> allocations,
        IEnumerable<BudgetActual> actuals,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(actuals);

        List<BudgetAllocation> plans = [.. allocations];

        if (plans.Count == 0)
        {
            return [];
        }

        Dictionary<(int Category, DateOnly Period), Money> spent = actuals
            .GroupBy(a => (a.CategoryId, a.PeriodStart))
            .ToDictionary(g => g.Key, g => Money.Sum(g.Select(a => a.Amount)).Abs());

        DateOnly firstBudgeted = plans.Min(p => p.PeriodStart);
        DateOnly start = Month(from);
        DateOnly finish = Month(to);

        // Walk from the earliest budgeted period so the rollover chain is complete, and
        // report only the window asked for.
        DateOnly cursor = Month(firstBudgeted) < start ? Month(firstBudgeted) : start;

        var carried = new Dictionary<int, Money>();
        var results = new List<BudgetPeriodResult>();

        // A hard ceiling: a budget set decades ago should not spin the loop for ever.
        for (int guard = 0; cursor <= finish && guard < 1_200; guard++, cursor = cursor.AddMonths(1))
        {
            var lines = new List<BudgetLineResult>();

            foreach (BudgetAllocation plan in plans.Where(p => Month(p.PeriodStart) == cursor))
            {
                Money rolloverIn = plan.RollsOver
                    ? carried.GetValueOrDefault(plan.CategoryId)
                    : Money.Zero;

                var line = new BudgetLineResult
                {
                    CategoryId = plan.CategoryId,
                    CategoryPath = plan.CategoryPath,
                    Kind = plan.Kind,
                    PeriodStart = cursor,
                    Budgeted = plan.Amount.Abs(),
                    RolloverIn = rolloverIn,
                    Actual = spent.GetValueOrDefault((plan.CategoryId, cursor)),
                    RollsOver = plan.RollsOver,
                };

                lines.Add(line);

                // An overspend does not become a debt against next month: the budget for
                // April is what was set for April. Only an unspent remainder travels.
                carried[plan.CategoryId] = line.RolloverOut;
            }

            lines.Sort((left, right) => string.Compare(
                left.CategoryPath,
                right.CategoryPath,
                StringComparison.CurrentCultureIgnoreCase));

            if (cursor >= start)
            {
                results.Add(new BudgetPeriodResult
                {
                    PeriodStart = cursor,
                    Label = cursor.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
                    Lines = lines,
                });
            }
        }

        return results;
    }

    /// <summary>
    /// A year's budget rolled into one line per category.
    /// </summary>
    /// <remarks>
    /// Rollover is deliberately ignored here. Over a whole year it nets out to nothing — what
    /// one month carried forward another received — and showing it would double-count.
    /// </remarks>
    public static BudgetPeriodResult ForYear(
        IEnumerable<BudgetAllocation> allocations,
        IEnumerable<BudgetActual> actuals,
        int year)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(actuals);

        List<BudgetAllocation> plans = [.. allocations.Where(a => a.PeriodStart.Year == year)];

        Dictionary<int, Money> spent = actuals
            .Where(a => a.PeriodStart.Year == year)
            .GroupBy(a => a.CategoryId)
            .ToDictionary(g => g.Key, g => Money.Sum(g.Select(a => a.Amount)).Abs());

        var lines = new List<BudgetLineResult>();

        foreach (IGrouping<int, BudgetAllocation> group in plans.GroupBy(p => p.CategoryId))
        {
            BudgetAllocation first = group.First();

            lines.Add(new BudgetLineResult
            {
                CategoryId = group.Key,
                CategoryPath = first.CategoryPath,
                Kind = first.Kind,
                PeriodStart = new DateOnly(year, 1, 1),
                Budgeted = Money.Sum(group.Select(p => p.Amount.Abs())),
                RolloverIn = Money.Zero,
                Actual = spent.GetValueOrDefault(group.Key),
                RollsOver = false,
            });
        }

        lines.Sort((left, right) => string.Compare(
            left.CategoryPath,
            right.CategoryPath,
            StringComparison.CurrentCultureIgnoreCase));

        return new BudgetPeriodResult
        {
            PeriodStart = new DateOnly(year, 1, 1),
            Label = year.ToString(CultureInfo.CurrentCulture),
            Lines = lines,
        };
    }

    private static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);
}
