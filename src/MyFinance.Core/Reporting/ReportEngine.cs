using System.Globalization;
using MyFinance.Core.Entities;
using MyFinance.Core.Investments;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Reporting;

/// <summary>
/// Aggregates transactions into the figures every report shows.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and returns plain rows rather than anything a chart library understands. That keeps
/// the arithmetic — which is what has to be right — testable with no database and no
/// Windows, and means the same numbers feed a chart, a table and an export without any of
/// them recomputing.
/// </para>
/// <para>
/// Transfers are excluded by default throughout. Money moved between the user's own accounts
/// is neither income nor spending, and counting it would inflate both sides of every total
/// by the same amount.
/// </para>
/// </remarks>
public static class ReportEngine
{
    /// <summary>The label used for allocations with no category.</summary>
    public const string UncategorizedLabel = "Uncategorized";

    /// <summary>The label used for transactions with no payee.</summary>
    public const string NoPayeeLabel = "(no payee)";

    /// <summary>
    /// Where the money went: outgoings grouped by category, largest first.
    /// </summary>
    /// <remarks>
    /// Grouped on the sign of the allocation rather than on the kind of account it sits in.
    /// Fifty pounds spent on a credit card is fifty pounds of spending exactly as it is from
    /// a chequing account, so a liability needs no special case here.
    /// </remarks>
    public static GroupedReport SpendingByCategory(
        IEnumerable<ReportEntry> entries,
        ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);

        return Group(
            "Spending by category",
            entries.Where(e => filter.Matches(e) && e.IsSpending),
            filter,
            e => CategoryKey(e, filter),
            e => CategoryLabel(e, filter));
    }

    /// <summary>Where the money came from: income grouped by category.</summary>
    public static GroupedReport IncomeByCategory(
        IEnumerable<ReportEntry> entries,
        ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);

        return Group(
            "Income by category",
            entries.Where(e => filter.Matches(e) && e.IsIncome),
            filter,
            e => CategoryKey(e, filter),
            e => CategoryLabel(e, filter));
    }

    /// <summary>Outgoings grouped by who was paid.</summary>
    public static GroupedReport SpendingByPayee(
        IEnumerable<ReportEntry> entries,
        ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);

        return Group(
            "Spending by payee",
            entries.Where(e => filter.Matches(e) && e.IsSpending),
            filter,
            e => e.PayeeId,
            e => e.PayeeName ?? NoPayeeLabel);
    }

    /// <summary>Income and spending side by side, period by period.</summary>
    public static TimeSeriesReport IncomeAndSpendingOverTime(
        IEnumerable<ReportEntry> entries,
        ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);

        List<ReportEntry> matched = [.. entries.Where(filter.Matches)];

        if (matched.Count == 0)
        {
            return TimeSeriesReport.Empty;
        }

        var periods = new List<ReportPeriodTotal>();

        foreach (IGrouping<DateOnly, ReportEntry> group in matched
            .GroupBy(e => PeriodStart(e.Date, filter.Period))
            .OrderBy(g => g.Key))
        {
            periods.Add(new ReportPeriodTotal(
                group.Key,
                PeriodLabel(group.Key, filter.Period),
                Money.Sum(group.Where(e => e.IsIncome).Select(e => e.Amount)),
                Money.Sum(group.Where(e => e.IsSpending).Select(e => e.Amount))));
        }

        // Periods with nothing in them are filled in, so a gap reads as a month with no
        // activity rather than as a month that never existed.
        return new TimeSeriesReport
        {
            Title = "Income and spending over time",
            Periods = FillGaps(periods, filter.Period),
        };
    }

    /// <summary>Every transaction the filter covers, in date order — the drill-down list.</summary>
    public static IReadOnlyList<ReportEntry> Transactions(
        IEnumerable<ReportEntry> entries,
        ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);

        return
        [
            .. entries
                .Where(filter.Matches)
                .OrderBy(e => e.Date)
                .ThenBy(e => e.TransactionId)
        ];
    }

    /// <summary>
    /// Net worth at each period end.
    /// </summary>
    /// <remarks>
    /// Takes opening balances separately from the movements, because net worth at a date is
    /// everything that ever happened up to it — not just what falls inside the report window.
    /// </remarks>
    /// <param name="openingBalances">Each account's balance before the first period.</param>
    /// <param name="movements">Every non-void transaction, whatever its date.</param>
    /// <param name="from">First period start.</param>
    /// <param name="to">Last date to plot.</param>
    /// <param name="period">How to step.</param>
    /// <param name="investments">
    /// Holding activity, replayed to each point so a historical figure reflects what was held
    /// then rather than what is held now. Null when the book has no investment accounts.
    /// </param>
    /// <param name="prices">Every price on record, for valuing those holdings.</param>
    public static IReadOnlyList<NetWorthPoint> NetWorthOverTime(
        IReadOnlyDictionary<int, (AccountGroup Group, Money Opening)> openingBalances,
        IEnumerable<ReportEntry> movements,
        DateOnly from,
        DateOnly to,
        ReportPeriod period,
        IReadOnlyList<InvestmentMovement>? investments = null,
        IReadOnlyList<SecurityPrice>? prices = null)
    {
        ArgumentNullException.ThrowIfNull(openingBalances);
        ArgumentNullException.ThrowIfNull(movements);

        List<ReportEntry> rows = [.. movements.Where(m => !m.IsVoid)];
        var points = new List<NetWorthPoint>();

        for (DateOnly cursor = PeriodStart(from, period); cursor <= to; cursor = NextPeriod(cursor, period))
        {
            DateOnly end = Min(NextPeriod(cursor, period).AddDays(-1), to);

            Money assets = Money.Zero;
            Money liabilities = Money.Zero;

            foreach ((int accountId, (AccountGroup group, Money opening)) in openingBalances)
            {
                Money balance = opening + Money.Sum(rows
                    .Where(r => r.AccountId == accountId && r.Date <= end)
                    .Select(r => r.Amount));

                if (group == AccountGroup.Credit)
                {
                    liabilities += balance;
                }
                else
                {
                    assets += balance;
                }
            }

            // Holdings, as they stood on this date rather than as they stand now, and valued
            // at the most recent price on or before it. A share bought in 2024 does not
            // appear in a 2019 figure.
            if (investments is { Count: > 0 })
            {
                foreach ((_, HoldingState state) in HoldingCalculator.StateAt(investments, end))
                {
                    assets += HoldingCalculator
                        .Value(state, prices ?? [], end)
                        .Amount;
                }
            }

            points.Add(new NetWorthPoint(end, PeriodLabel(cursor, period), assets, liabilities));
        }

        return points;
    }

    /// <summary>How each group's total changed between two windows.</summary>
    public static ComparisonReport CompareByCategory(
        IEnumerable<ReportEntry> entries,
        ReportFilter first,
        ReportFilter second,
        string firstLabel,
        string secondLabel)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        List<ReportEntry> all = [.. entries];

        Dictionary<int, Money> firstTotals = Totals(all, first);
        Dictionary<int, Money> secondTotals = Totals(all, second);

        Dictionary<int, string> labels = all
            .Where(e => first.Matches(e) || second.Matches(e))
            .GroupBy(e => SurrogateKey(e, first))
            .ToDictionary(g => g.Key, g => CategoryLabel(g.First(), first));

        var rows = new List<ComparisonRow>();

        foreach (int key in firstTotals.Keys.Union(secondTotals.Keys))
        {
            rows.Add(new ComparisonRow(
                key == UncategorizedKey ? null : key,
                labels.GetValueOrDefault(key) ?? UncategorizedLabel,
                firstTotals.GetValueOrDefault(key),
                secondTotals.GetValueOrDefault(key)));
        }

        // Ordered by how much the figure moved, so what changed is at the top rather than
        // whatever happens to be biggest in absolute terms. Ties break on the label so the
        // order is the same every time the report is run.
        rows = [.. rows
            .OrderByDescending(r => r.Change.Abs())
            .ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase)];

        return new ComparisonReport
        {
            Title = "Spending by category, compared",
            Rows = rows,
            FirstLabel = firstLabel,
            SecondLabel = secondLabel,
        };
    }

    /// <summary>The first day of the period a date falls in.</summary>
    public static DateOnly PeriodStart(DateOnly date, ReportPeriod period) => period switch
    {
        ReportPeriod.Yearly => new DateOnly(date.Year, 1, 1),
        ReportPeriod.Quarterly => new DateOnly(date.Year, (((date.Month - 1) / 3) * 3) + 1, 1),
        _ => new DateOnly(date.Year, date.Month, 1),
    };

    /// <summary>How a period reads on an axis.</summary>
    public static string PeriodLabel(DateOnly start, ReportPeriod period) => period switch
    {
        ReportPeriod.Yearly => start.Year.ToString(CultureInfo.CurrentCulture),
        ReportPeriod.Quarterly => string.Create(
            CultureInfo.CurrentCulture,
            $"Q{((start.Month - 1) / 3) + 1} {start.Year}"),
        _ => start.ToString("MMM yyyy", CultureInfo.CurrentCulture),
    };

    private static DateOnly NextPeriod(DateOnly start, ReportPeriod period) => period switch
    {
        ReportPeriod.Yearly => start.AddYears(1),
        ReportPeriod.Quarterly => start.AddMonths(3),
        _ => start.AddMonths(1),
    };

    /// <summary>
    /// Stands in for "no category" where a dictionary key cannot be null.
    /// </summary>
    /// <remarks>
    /// A real category id is a positive identity column, so this can never collide with one.
    /// </remarks>
    private const int UncategorizedKey = int.MinValue;

    private static Dictionary<int, Money> Totals(IReadOnlyList<ReportEntry> all, ReportFilter filter) =>
        all.Where(e => filter.Matches(e) && e.IsSpending)
            .GroupBy(e => SurrogateKey(e, filter))
            .ToDictionary(g => g.Key, g => Money.Sum(g.Select(e => e.Amount)));

    private static int SurrogateKey(ReportEntry entry, ReportFilter filter) =>
        CategoryKey(entry, filter) ?? UncategorizedKey;

    private static int? CategoryKey(ReportEntry entry, ReportFilter filter) =>
        filter.RollUpToParent && entry.CategoryParent is not null
            ? entry.CategoryParent.GetHashCode(StringComparison.Ordinal)
            : entry.CategoryId;

    private static string CategoryLabel(ReportEntry entry, ReportFilter filter)
    {
        if (entry.CategoryPath is null)
        {
            return UncategorizedLabel;
        }

        return filter.RollUpToParent ? entry.CategoryParent ?? entry.CategoryPath : entry.CategoryPath;
    }

    /// <summary>
    /// Groups entries, sorts by magnitude, and works out each row's share.
    /// </summary>
    private static GroupedReport Group(
        string title,
        IEnumerable<ReportEntry> matched,
        ReportFilter filter,
        Func<ReportEntry, int?> key,
        Func<ReportEntry, string> label)
    {
        List<ReportEntry> rows = [.. matched];

        if (rows.Count == 0)
        {
            return GroupedReport.Empty with { Title = title };
        }

        Money total = Money.Sum(rows.Select(e => e.Amount));

        var groups = new List<ReportGroup>();

        foreach (IGrouping<int?, ReportEntry> group in rows.GroupBy(key))
        {
            Money amount = Money.Sum(group.Select(e => e.Amount));

            groups.Add(new ReportGroup(
                group.Key,
                label(group.First()),
                amount,
                group.Select(e => e.TransactionId).Distinct().Count())
            {
                // Computed against the magnitude of the total, so a report of outgoings —
                // where every figure is negative — still yields positive shares.
                Share = total.IsZero
                    ? 0
                    : (decimal)Math.Abs(amount.MinorUnits) / Math.Abs(total.MinorUnits),
            });
        }

        // Sorted with an explicit tiebreak rather than by List.Sort, which is unstable: two
        // categories that happen to total the same would otherwise swap places between runs,
        // and a report that reorders itself for no reason reads as corrupted data.
        groups = [.. groups
            .OrderByDescending(g => g.Magnitude)
            .ThenBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase)];

        List<ReportEntry> uncategorized = [.. rows.Where(e => e.IsUncategorized)];

        return new GroupedReport
        {
            Title = title,
            Rows = groups,
            Total = total,
            EntryCount = rows.Select(e => e.TransactionId).Distinct().Count(),
            UncategorizedCount = uncategorized.Select(e => e.TransactionId).Distinct().Count(),
            UncategorizedAmount = Money.Sum(uncategorized.Select(e => e.Amount)),
        };
    }

    /// <summary>
    /// Inserts empty periods so a series has no holes in it.
    /// </summary>
    /// <remarks>
    /// A month with no transactions is a real fact about the data. Leaving it out would make
    /// a chart's axis lie about the passage of time and a trend line join across a gap that
    /// is not there.
    /// </remarks>
    private static IReadOnlyList<ReportPeriodTotal> FillGaps(
        List<ReportPeriodTotal> periods,
        ReportPeriod period)
    {
        if (periods.Count < 2)
        {
            return periods;
        }

        var filled = new List<ReportPeriodTotal>();
        DateOnly cursor = periods[0].Start;
        DateOnly last = periods[^1].Start;

        var byStart = periods.ToDictionary(p => p.Start);

        while (cursor <= last)
        {
            filled.Add(byStart.TryGetValue(cursor, out ReportPeriodTotal? found)
                ? found
                : new ReportPeriodTotal(cursor, PeriodLabel(cursor, period), Money.Zero, Money.Zero));

            cursor = NextPeriod(cursor, period);
        }

        return filled;
    }

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
