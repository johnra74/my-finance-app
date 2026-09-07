using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Entities;

/// <summary>
/// One line in one account's register.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Amount"/> is signed from the owning account's point of view: negative is money
/// leaving, positive is money arriving. On a credit card that means purchases are negative
/// and payments positive, so the account balance is negative while a balance is owed — which
/// is how Microsoft Money shows it, e.g. Everyday Rewards Card at ($2,756.30).
/// </para>
/// <para>
/// A transfer is stored as <b>two</b> rows, one in each account, pointing at each other via
/// <see cref="TransferPeerId"/>. That keeps each register independently correct and lets
/// reports exclude transfers wholesale so moving money between your own accounts never looks
/// like income or spending.
/// </para>
/// </remarks>
public class Transaction
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public Account? Account { get; set; }

    /// <summary>Date the transaction is posted against the register.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Cheque number or reference, e.g. "1236" or "ATM".</summary>
    public string? Number { get; set; }

    public int? PayeeId { get; set; }

    public Payee? Payee { get; set; }

    public string? Memo { get; set; }

    /// <summary>Signed total. Must always equal the sum of <see cref="Splits"/>.</summary>
    public Money Amount { get; set; }

    public ClearedStatus ClearedStatus { get; set; }

    /// <summary>The matching row in the other account when this is one leg of a transfer.</summary>
    public int? TransferPeerId { get; set; }

    public Transaction? TransferPeer { get; set; }

    /// <summary>
    /// The bank's own unique id for this transaction (OFX FITID). The authoritative key for
    /// detecting that a download overlaps transactions already imported.
    /// </summary>
    public string? FitId { get; set; }

    public int? ImportBatchId { get; set; }

    public ImportBatch? ImportBatch { get; set; }

    /// <summary>
    /// Voided rows stay visible in the register for audit but contribute zero to balances.
    /// </summary>
    public bool IsVoid { get; set; }

    /// <summary>Set when this row was generated from a scheduled transaction.</summary>
    public int? ScheduledTransactionId { get; set; }

    public ScheduledTransaction? ScheduledTransaction { get; set; }

    /// <summary>
    /// Ordering tiebreaker for rows sharing a <see cref="Date"/>, so the running balance is
    /// stable across sessions rather than depending on database row order.
    /// </summary>
    public int SequenceInDay { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }

    /// <summary>
    /// Category allocation. Always at least one row, even for an uncategorized transaction,
    /// so every report can aggregate over one uniform table without special cases.
    /// </summary>
    public ICollection<TransactionSplit> Splits { get; set; } = [];

    public bool IsTransfer => TransferPeerId is not null;

    /// <summary>Amount that actually affects the balance; voided rows contribute nothing.</summary>
    public Money EffectiveAmount => IsVoid ? Money.Zero : Amount;

    /// <summary>True when no split carries a category, i.e. it needs triage.</summary>
    public bool IsUncategorized => Splits.All(s => s.CategoryId is null);

    /// <summary>True when the category allocation is spread across more than one category.</summary>
    public bool IsSplit => Splits.Count > 1;
}

/// <summary>One category allocation within a transaction.</summary>
public class TransactionSplit
{
    public int Id { get; set; }

    public int TransactionId { get; set; }

    public Transaction? Transaction { get; set; }

    /// <summary>Null means uncategorized — the state the triage worklist exists to clear.</summary>
    public int? CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>Signed, same convention as the parent transaction.</summary>
    public Money Amount { get; set; }

    public string? Memo { get; set; }

    public int SortOrder { get; set; }
}
