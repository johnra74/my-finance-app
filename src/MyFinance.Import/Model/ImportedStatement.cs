using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Import.Model;

/// <summary>What kind of account a downloaded file covers.</summary>
public enum ImportedAccountKind
{
    Bank = 0,
    CreditCard = 1,

    /// <summary>Brokerage. Recognised so it can be refused clearly rather than half-read.</summary>
    Investment = 2,

    /// <summary>The file did not say. QIF often does not.</summary>
    Unknown = 3,
}

/// <summary>The account a file belongs to, as the file identifies it.</summary>
/// <param name="Kind">Bank, credit card, or unstated.</param>
/// <param name="BankId">Routing or issuer id. OFX only; absent from QIF.</param>
/// <param name="AccountId">The account identifier. OFX only.</param>
/// <param name="Name">The account's name as the file gives it. QIF's only identifier.</param>
/// <param name="MappedType">The account type this maps onto, when the file said.</param>
public sealed record ImportedAccountInfo(
    ImportedAccountKind Kind,
    string? BankId,
    string? AccountId,
    string? Name,
    AccountType? MappedType);

/// <summary>One category allocation the file itself supplied.</summary>
/// <param name="CategoryPath">Display path as written, e.g. "Food:Groceries".</param>
/// <param name="Memo">Note attached to this line.</param>
/// <param name="Amount">Signed, same convention as the parent transaction.</param>
public sealed record ImportedSplit(string? CategoryPath, string? Memo, Money Amount);

/// <summary>
/// One transaction from a downloaded file, in a form independent of which format it came in.
/// </summary>
/// <remarks>
/// OFX and QIF describe overlapping but different things — OFX has a bank-issued reference
/// and no category, QIF has the user's own category and no reference — so this carries the
/// union and lets everything downstream of the readers work the same way for both.
/// </remarks>
public sealed record ImportedTransaction
{
    /// <summary>
    /// The bank's own unique id, from OFX's FITID. Null for QIF, which has no such thing —
    /// which is why duplicate detection has to fall back to matching on the amount and date.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>The file's own type word, e.g. OFX's "DEBIT". Used to sanity-check signs.</summary>
    public string? TransactionType { get; init; }

    /// <summary>Date the item posted, taken literally from the file.</summary>
    public required DateOnly Posted { get; init; }

    public required Money Amount { get; init; }

    /// <summary>Raw payee text as the file wrote it.</summary>
    public string? Name { get; init; }

    public string? Memo { get; init; }

    /// <summary>Cheque number or reference.</summary>
    public string? CheckNumber { get; init; }

    /// <summary>
    /// Cleared state the file asserted. QIF records one per row; OFX does not, and an OFX
    /// row is treated as cleared because a downloaded item has by definition reached the bank.
    /// </summary>
    public ClearedStatus? Cleared { get; init; }

    /// <summary>
    /// The category the file itself named, when it had one. QIF carries the user's own
    /// categorization; OFX never does.
    /// </summary>
    public string? CategoryPath { get; init; }

    /// <summary>
    /// The other account, when the file marks this as a transfer between the user's own
    /// accounts. Named rather than identified, because QIF only gives a name.
    /// </summary>
    public string? TransferAccountName { get; init; }

    /// <summary>Category allocation the file supplied. Empty when it supplied none.</summary>
    public IReadOnlyList<ImportedSplit> Splits { get; init; } = [];

    /// <summary>Set when the file says this row replaces one sent earlier.</summary>
    public string? CorrectsExternalId { get; init; }

    /// <summary>
    /// The merchant's industry code, when the bank supplied one.
    /// </summary>
    /// <remarks>
    /// OFX calls this SIC. It is optional and many banks omit it, but where it is present it
    /// is a statement of fact about the merchant rather than an inference from its name.
    /// </remarks>
    public string? MerchantCode { get; init; }

    public bool IsTransfer => !string.IsNullOrWhiteSpace(TransferAccountName);

    public bool IsSplit => Splits.Count > 1;

    /// <summary>Everything the file wrote to describe this row, joined for the memo field.</summary>
    public string RawDescriptor =>
        string.Join(
            " ",
            new[] { Name, Memo }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
}

/// <summary>A balance the file asserts, with the date it applies to.</summary>
/// <param name="Amount">The stated balance.</param>
/// <param name="AsOf">When it was taken.</param>
public sealed record ImportedBalance(Money Amount, DateOnly? AsOf);

/// <summary>One statement extracted from a file, whatever format it arrived in.</summary>
public sealed record ImportedStatement
{
    public required ImportFormat Format { get; init; }

    public required ImportedAccountInfo Account { get; init; }

    public string? CurrencyCode { get; init; }

    public DateOnly? PeriodStart { get; init; }

    public DateOnly? PeriodEnd { get; init; }

    public required IReadOnlyList<ImportedTransaction> Transactions { get; init; }

    /// <summary>Closing balance the file states. The cross-check for the amounts' signs.</summary>
    public ImportedBalance? LedgerBalance { get; init; }

    public ImportedBalance? AvailableBalance { get; init; }

    public string? Institution { get; init; }

    /// <summary>Category paths the file referred to, for reconciling against the book's own.</summary>
    public IReadOnlyList<string> CategoryPaths =>
    [
        .. Transactions
            .SelectMany(t => t.Splits.Select(s => s.CategoryPath).Append(t.CategoryPath))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
    ];

    public DateOnly? FirstPosted =>
        Transactions.Count == 0 ? null : Transactions.Min(t => t.Posted);

    public DateOnly? LastPosted =>
        Transactions.Count == 0 ? null : Transactions.Max(t => t.Posted);

    /// <summary>Sum of every transaction as stated, before any sign correction.</summary>
    public Money Total => Money.Sum(Transactions.Select(t => t.Amount));
}

/// <summary>Everything read from one file, whatever its format.</summary>
public sealed record ImportedFile
{
    public required ImportFormat Format { get; init; }

    public required IReadOnlyList<ImportedStatement> Statements { get; init; }

    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }

    public bool HasBlockingError => Diagnostics.Any(d => d.Severity == ImportSeverity.Error);

    public IEnumerable<ImportDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == ImportSeverity.Error);

    public IEnumerable<ImportDiagnostic> Warnings =>
        Diagnostics.Where(d => d.Severity == ImportSeverity.Warning);
}
