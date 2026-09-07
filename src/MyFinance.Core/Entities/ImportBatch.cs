using MyFinance.Core.Enums;

namespace MyFinance.Core.Entities;

/// <summary>
/// One import run, recorded so a bad import can be undone as a single unit rather than
/// unpicked transaction by transaction.
/// </summary>
public class ImportBatch
{
    public int Id { get; set; }

    /// <summary>Original file name, for the import history list.</summary>
    public string? SourceFileName { get; set; }

    public ImportFormat Format { get; set; }

    public int? AccountId { get; set; }

    public Account? Account { get; set; }

    public DateTimeOffset ImportedUtc { get; set; }

    public int TransactionsAdded { get; set; }

    public int DuplicatesSkipped { get; set; }

    public int PayeesCreated { get; set; }

    public int CategoriesCreated { get; set; }

    /// <summary>Earliest and latest posted date seen in the file.</summary>
    public DateOnly? PeriodStart { get; set; }

    public DateOnly? PeriodEnd { get; set; }

    /// <summary>Cleared once the batch has been rolled back.</summary>
    public bool IsReverted { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
