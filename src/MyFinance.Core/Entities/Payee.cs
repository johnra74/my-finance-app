namespace MyFinance.Core.Entities;

/// <summary>Who money was paid to or received from.</summary>
public class Payee
{
    public int Id { get; set; }

    /// <summary>Display name, e.g. "Woodgrove Mortgage".</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Upper-cased, punctuation-stripped form used to match noisy import descriptors
    /// ("SQ *BLUE BOTTLE 1234 NEW YORK NY") back to this payee. Maintained by the importer.
    /// </summary>
    public required string NormalizedName { get; set; }

    /// <summary>
    /// Category last assigned to this payee, used to pre-fill the register and to seed
    /// auto-categorization on import.
    /// </summary>
    public int? LastCategoryId { get; set; }

    public Category? LastCategory { get; set; }

    /// <summary>Amount last used for this payee, offered as a default in the register.</summary>
    public Primitives.Money? LastAmount { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];

    /// <summary>Alternate raw descriptors that should resolve to this payee.</summary>
    public ICollection<PayeeAlias> Aliases { get; set; } = [];
}

/// <summary>
/// A raw bank descriptor mapped onto a payee, so the same merchant recognised once stays
/// recognised on every later download.
/// </summary>
public class PayeeAlias
{
    public int Id { get; set; }

    public int PayeeId { get; set; }

    public Payee? Payee { get; set; }

    /// <summary>Normalized form of the raw descriptor as it arrives from the bank.</summary>
    public required string NormalizedPattern { get; set; }
}
