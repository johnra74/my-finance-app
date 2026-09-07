using System.Windows.Documents;
using MyFinance.App.ViewModels.Pages;
using MyFinance.Core.Printing;

namespace MyFinance.App.Printing;

/// <summary>
/// A register, on paper.
/// </summary>
/// <remarks>
/// Every cell comes from the row view model the grid is already bound to — <c>DateText</c>,
/// <c>PaymentText</c>, <c>BalanceText</c> — rather than being formatted again here. That is
/// what keeps the printout and the screen from drifting apart, which matters more than it
/// sounds: the printed copy is the one that leaves the building.
/// </remarks>
internal static class RegisterDocument
{
    /// <summary>How many pages this would be, without composing them.</summary>
    /// <remarks>
    /// Wanted before the print dialog opens, not after. A twenty-five-year register is
    /// thousands of pages and somebody will ask for one by accident.
    /// </remarks>
    public static int PageCount(int rowCount, PageGeometry geometry)
    {
        int perPage = PrintedTable.RowsPerPage(geometry);
        return Math.Max(1, (rowCount + perPage - 1) / perPage);
    }

    public static FixedDocument Build(
        string accountName,
        IReadOnlyList<RegisterRowViewModel> rows,
        ColumnFit fit,
        PageGeometry geometry,
        DateOnly? from = null,
        DateOnly? to = null,
        string? filterDescription = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(fit);

        string subtitle = PrintedTable.PeriodText(from, to);

        if (!string.IsNullOrWhiteSpace(filterDescription))
        {
            // The filter is part of what the page means. A printed register that silently
            // showed a subset would be worse than useless to anyone checking it.
            subtitle += $"  ·  {filterDescription}";
        }

        subtitle += $"  ·  {rows.Count} {(rows.Count == 1 ? "transaction" : "transactions")}";

        return PrintedTable.Build(
            accountName,
            subtitle,
            fit,
            [.. rows.Select(Cells)],
            geometry,
            NoteFor(fit));
    }

    /// <summary>Says what was left out, rather than letting the reader assume nothing was.</summary>
    private static string? NoteFor(ColumnFit fit)
    {
        if (fit.Dropped.Count == 0)
        {
            return fit.IsCramped
                ? "These columns are wider than the page and may be trimmed when printed."
                : null;
        }

        return $"Columns not shown: {string.Join(", ", fit.Dropped.Select(c => c.Header))}.";
    }

    private static Func<string, string> Cells(RegisterRowViewModel row) => key => key switch
    {
        "date" => row.DateText,
        "number" => row.Number ?? string.Empty,
        "payee" => row.PayeeName,
        "category" => row.CategoryText,
        "memo" => row.Memo ?? string.Empty,
        "cleared" => row.ClearedMark,
        "payment" => row.PaymentText,
        "deposit" => row.DepositText,
        "balance" => row.BalanceText,
        _ => string.Empty,
    };
}
