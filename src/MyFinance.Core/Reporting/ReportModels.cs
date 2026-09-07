using System.Globalization;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Reporting;

/// <summary>One grouped line of a report: a label and what it totals.</summary>
/// <param name="Key">The grouping key — a category or payee id, or null for "unassigned".</param>
/// <param name="Label">How the group reads on screen.</param>
/// <param name="Amount">Signed total for the group.</param>
/// <param name="Count">How many allocations it covers.</param>
public sealed record ReportGroup(int? Key, string Label, Money Amount, int Count)
{
    /// <summary>The magnitude, which is what a spending chart plots.</summary>
    public Money Magnitude => Amount.Abs();

    /// <summary>Share of the report's total, between zero and one. Set by the engine.</summary>
    public decimal Share { get; init; }

    public string ShareText => Share.ToString("P1", CultureInfo.CurrentCulture);
}

/// <summary>One period of a time series.</summary>
/// <param name="Start">First day of the period.</param>
/// <param name="Label">How it reads on an axis, e.g. "Mar 2026".</param>
/// <param name="Income">Everything that came in, as a positive figure.</param>
/// <param name="Spending">Everything that went out, as a negative figure.</param>
public sealed record ReportPeriodTotal(DateOnly Start, string Label, Money Income, Money Spending)
{
    /// <summary>What was left over. Negative means more went out than came in.</summary>
    public Money Net => Income + Spending;

    /// <summary>Spending as a positive figure, for a chart that plots bars upward.</summary>
    public Money SpendingMagnitude => Spending.Abs();
}

/// <summary>A grouped report: the rows, the total, and what was left out.</summary>
public sealed record GroupedReport
{
    public static GroupedReport Empty { get; } = new()
    {
        Title = string.Empty,
        Rows = [],
        Total = Money.Zero,
        EntryCount = 0,
    };

    public required string Title { get; init; }

    /// <summary>Groups, largest first.</summary>
    public required IReadOnlyList<ReportGroup> Rows { get; init; }

    public required Money Total { get; init; }

    public required int EntryCount { get; init; }

    /// <summary>
    /// Allocations with no category that the report covers, and what they come to.
    /// </summary>
    /// <remarks>
    /// Surfaced separately because an uncategorized backlog silently distorts every
    /// percentage on the report. Microsoft Money puts the same warning above its charts.
    /// </remarks>
    public int UncategorizedCount { get; init; }

    public Money UncategorizedAmount { get; init; }

    public bool HasUncategorized => UncategorizedCount > 0;

    public string UncategorizedWarning => UncategorizedCount == 0
        ? string.Empty
        : string.Create(
            CultureInfo.CurrentCulture,
            $"{UncategorizedCount} transaction{(UncategorizedCount == 1 ? string.Empty : "s")}, totalling {UncategorizedAmount.Abs().ToString("C", CultureInfo.CurrentCulture)}, have no category assigned.");

    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>A time series report.</summary>
public sealed record TimeSeriesReport
{
    public static TimeSeriesReport Empty { get; } = new() { Title = string.Empty, Periods = [] };

    public required string Title { get; init; }

    public required IReadOnlyList<ReportPeriodTotal> Periods { get; init; }

    public Money TotalIncome => Money.Sum(Periods.Select(p => p.Income));

    public Money TotalSpending => Money.Sum(Periods.Select(p => p.Spending));

    public Money Net => TotalIncome + TotalSpending;

    /// <summary>Average net movement per period, for the "you save about X a month" line.</summary>
    public Money AverageNet => Periods.Count == 0
        ? Money.Zero
        : Money.FromMinorUnits(Net.MinorUnits / Periods.Count);

    public bool IsEmpty => Periods.Count == 0;
}

/// <summary>One account's standing, for the balances and net worth reports.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Name">Its name.</param>
/// <param name="Group">Which heading it subtotals under.</param>
/// <param name="Balance">Its balance, signed so a debt is negative.</param>
public sealed record AccountBalanceRow(int AccountId, string Name, Enums.AccountGroup Group, Money Balance);

/// <summary>Net worth at one point in time.</summary>
/// <param name="Date">When.</param>
/// <param name="Label">How it reads on an axis.</param>
/// <param name="Assets">What is held, as a positive figure.</param>
/// <param name="Liabilities">What is owed, as a negative figure.</param>
public sealed record NetWorthPoint(DateOnly Date, string Label, Money Assets, Money Liabilities)
{
    public Money NetWorth => Assets + Liabilities;

    /// <summary>Debt as a positive figure, for a chart that plots it as a bar.</summary>
    public Money LiabilitiesMagnitude => Liabilities.Abs();
}

/// <summary>How one group changed between two periods.</summary>
/// <param name="Key">Category or payee id.</param>
/// <param name="Label">How it reads.</param>
/// <param name="First">Total in the earlier period.</param>
/// <param name="Second">Total in the later period.</param>
public sealed record ComparisonRow(int? Key, string Label, Money First, Money Second)
{
    public Money Change => Second - First;

    /// <summary>
    /// Proportional change, or null when the earlier period was zero.
    /// </summary>
    /// <remarks>
    /// Null rather than infinity: something going from nothing to something has no
    /// meaningful percentage, and showing one would be worse than showing none.
    /// </remarks>
    public decimal? ChangeShare => First.IsZero
        ? null
        : (decimal)Change.MinorUnits / Math.Abs(First.MinorUnits);

    public string ChangeShareText => ChangeShare is decimal share
        ? share.ToString("P1", CultureInfo.CurrentCulture)
        : "—";

    /// <summary>True when more went out in the later period than the earlier one.</summary>
    public bool SpendingRose => Change.Abs() > Money.Zero && Second.Abs() > First.Abs();
}

/// <summary>A comparison between two windows.</summary>
public sealed record ComparisonReport
{
    public required string Title { get; init; }

    public required IReadOnlyList<ComparisonRow> Rows { get; init; }

    public required string FirstLabel { get; init; }

    public required string SecondLabel { get; init; }

    public Money FirstTotal => Money.Sum(Rows.Select(r => r.First));

    public Money SecondTotal => Money.Sum(Rows.Select(r => r.Second));

    public Money Change => SecondTotal - FirstTotal;

    public bool IsEmpty => Rows.Count == 0;
}
