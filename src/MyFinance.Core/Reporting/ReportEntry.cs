using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Reporting;

/// <summary>
/// One category allocation, flattened with everything a report needs to group it by.
/// </summary>
/// <remarks>
/// Reports aggregate over splits rather than transactions, because a transaction divided
/// between groceries and household belongs to both categories and to neither exclusively.
/// Every transaction carries at least one split, so nothing is lost by working at this level
/// and no report needs a fallback path for the undivided case.
/// </remarks>
public sealed record ReportEntry
{
    public required int TransactionId { get; init; }

    public required int AccountId { get; init; }

    public required string AccountName { get; init; }

    public required AccountGroup AccountGroup { get; init; }

    public required DateOnly Date { get; init; }

    public int? PayeeId { get; init; }

    public string? PayeeName { get; init; }

    public int? CategoryId { get; init; }

    /// <summary>Display path, e.g. "Food : Groceries". Null when uncategorized.</summary>
    public string? CategoryPath { get; init; }

    /// <summary>The heading alone, for reports that roll subcategories up.</summary>
    public string? CategoryParent { get; init; }

    public CategoryKind? CategoryKind { get; init; }

    /// <summary>Signed from the owning account's point of view: negative is money out.</summary>
    public required Money Amount { get; init; }

    /// <summary>
    /// True when this is one leg of a movement between the user's own accounts.
    /// </summary>
    /// <remarks>
    /// Excluded from every spending and income report. Moving money from chequing to savings
    /// is not income in one and spending in the other; counting it would inflate both sides
    /// of every total by the same amount and make the numbers useless.
    /// </remarks>
    public bool IsTransfer { get; init; }

    /// <summary>
    /// True when this is the cash side of buying, selling or being paid by a security.
    /// </summary>
    /// <remarks>
    /// Excluded from spending and income for the same reason as a transfer, and identified the
    /// same structural way rather than by a heuristic: buying shares is not spending, it is
    /// moving money from cash into an asset you still own. Counting it would make every month
    /// you invested in look like a month you overspent in.
    /// </remarks>
    public bool IsInvestment { get; init; }

    public bool IsVoid { get; init; }

    public string? Memo { get; init; }

    public bool IsUncategorized => CategoryId is null;

    /// <summary>Money leaving, as it appears in a spending report.</summary>
    public bool IsSpending => Amount.IsNegative;

    public bool IsIncome => Amount.IsPositive;
}

/// <summary>How a report groups its rows over time.</summary>
public enum ReportPeriod
{
    Monthly = 0,
    Quarterly = 1,
    Yearly = 2,
}

/// <summary>What a report covers.</summary>
public sealed record ReportFilter
{
    public static ReportFilter Everything { get; } = new();

    /// <summary>Inclusive lower bound, or null for no lower bound.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Inclusive upper bound, or null for no upper bound.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Restricts to these accounts. Empty means all of them.</summary>
    public IReadOnlyList<int> AccountIds { get; init; } = [];

    /// <summary>Restricts to these categories. Empty means all of them.</summary>
    public IReadOnlyList<int> CategoryIds { get; init; } = [];

    public IReadOnlyList<int> PayeeIds { get; init; } = [];

    /// <summary>
    /// Include movements between the user's own accounts. Off by default, and should stay
    /// off for anything measuring income or spending.
    /// </summary>
    public bool IncludeTransfers { get; init; }

    /// <summary>Include allocations with no category, grouped under "Uncategorized".</summary>
    public bool IncludeUncategorized { get; init; } = true;

    /// <summary>Include voided rows. Off by default; a void contributes nothing to anything.</summary>
    public bool IncludeVoid { get; init; }

    public ReportPeriod Period { get; init; } = ReportPeriod.Monthly;

    /// <summary>Roll subcategories into their heading, so "Food" absorbs "Food : Coffee".</summary>
    public bool RollUpToParent { get; init; }

    /// <summary>Whether an entry passes the filter.</summary>
    public bool Matches(ReportEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!IncludeVoid && entry.IsVoid)
        {
            return false;
        }

        if (!IncludeTransfers && entry.IsTransfer)
        {
            return false;
        }

        // Buying shares is not spending — the money is still yours, in a different form.
        // Excluded the same structural way transfers are, and by the same switch, because a
        // report that wants to see transfers wants to see these too.
        if (!IncludeTransfers && entry.IsInvestment)
        {
            return false;
        }

        if (!IncludeUncategorized && entry.IsUncategorized)
        {
            return false;
        }

        if (From is DateOnly from && entry.Date < from)
        {
            return false;
        }

        if (To is DateOnly to && entry.Date > to)
        {
            return false;
        }

        if (AccountIds.Count > 0 && !AccountIds.Contains(entry.AccountId))
        {
            return false;
        }

        if (CategoryIds.Count > 0
            && (entry.CategoryId is not int category || !CategoryIds.Contains(category)))
        {
            return false;
        }

        if (PayeeIds.Count > 0 && (entry.PayeeId is not int payee || !PayeeIds.Contains(payee)))
        {
            return false;
        }

        return true;
    }
}
