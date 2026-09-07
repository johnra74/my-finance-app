using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Entities;

/// <summary>A budgeted amount for one category over one period.</summary>
public class BudgetLine
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>First day of the budgeted period.</summary>
    public DateOnly PeriodStart { get; set; }

    public BudgetPeriodType PeriodType { get; set; }

    /// <summary>Budgeted amount, always stored positive regardless of category kind.</summary>
    public Money Amount { get; set; }

    /// <summary>Carry any unspent remainder forward into the next period.</summary>
    public bool RollsOver { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// A category the user is watching closely, surfaced on the Home dashboard's spending
/// tracker tile.
/// </summary>
public class WatchedCategory
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    public int SortOrder { get; set; }
}
