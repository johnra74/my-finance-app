using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Model;

namespace MyFinance.Import.Ofx.Model;

/// <summary>What kind of account a statement covers.</summary>
public enum OfxAccountKind
{
    Bank = 0,
    CreditCard = 1,

    /// <summary>Brokerage. Recognised so it can be refused clearly rather than half-read.</summary>
    Investment = 2,
}

/// <summary>The account a statement belongs to, as the file identifies it.</summary>
/// <param name="Kind">Bank, credit card or investment.</param>
/// <param name="BankId">Routing or issuer id. Absent on most credit card statements.</param>
/// <param name="AccountId">The account identifier. Usually the full account number.</param>
/// <param name="AccountTypeText">The raw <c>ACCTTYPE</c>, e.g. "CHECKING".</param>
public sealed record OfxAccountInfo(
    OfxAccountKind Kind,
    string? BankId,
    string? AccountId,
    string? AccountTypeText)
{
    /// <summary>
    /// The account type this maps onto, or null when the file did not say.
    /// </summary>
    /// <remarks>
    /// A credit card statement is always a credit card whatever it claims, because
    /// <c>CCSTMTRS</c> is itself the declaration.
    /// </remarks>
    public AccountType? MappedType => Kind == OfxAccountKind.CreditCard
        ? AccountType.CreditCard
        : AccountTypeText?.Trim().ToUpperInvariant() switch
        {
            "CHECKING" => AccountType.Checking,
            "SAVINGS" => AccountType.Savings,
            "MONEYMRKT" => AccountType.MoneyMarket,
            "CD" => AccountType.CertificateOfDeposit,
            "CREDITLINE" => AccountType.LineOfCredit,
            _ => null,
        };
}

/// <summary>One transaction as the bank stated it, before any interpretation.</summary>
public sealed record OfxTransaction
{
    /// <summary>The bank's own unique id. The authoritative key for duplicate detection.</summary>
    public string? FitId { get; init; }

    /// <summary>Raw <c>TRNTYPE</c>, e.g. "DEBIT". Used to sanity-check the amount's sign.</summary>
    public string? TransactionType { get; init; }

    /// <summary>Date the item posted, taken literally from the file.</summary>
    public required DateOnly Posted { get; init; }

    /// <summary>
    /// The date the user actually transacted, when the bank distinguishes it from posting.
    /// Informational only; the register posts on <see cref="Posted"/>.
    /// </summary>
    public DateOnly? UserDate { get; init; }

    public required Money Amount { get; init; }

    /// <summary>Raw descriptor from <c>NAME</c> or the structured <c>PAYEE</c> aggregate.</summary>
    public string? Name { get; init; }

    public string? Memo { get; init; }

    /// <summary>Cheque number, from <c>CHECKNUM</c>.</summary>
    public string? CheckNumber { get; init; }

    /// <summary>Set when the bank says this row replaces one sent earlier.</summary>
    public string? CorrectsFitId { get; init; }

    /// <summary>The merchant's SIC code, when the bank sent one.</summary>
    public string? Sic { get; init; }

    /// <summary>Everything the bank wrote to describe this row, joined for the memo field.</summary>
    public string RawDescriptor =>
        string.Join(
            " ",
            new[] { Name, Memo }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
}

/// <summary>A balance the statement asserts, with the date it applies to.</summary>
/// <param name="Amount">The stated balance.</param>
/// <param name="AsOf">When it was taken.</param>
public sealed record OfxBalance(Money Amount, DateOnly? AsOf);

/// <summary>One statement extracted from a file. A file may contain several.</summary>
public sealed record OfxStatement
{
    public required OfxAccountInfo Account { get; init; }

    /// <summary>ISO currency code the statement is denominated in.</summary>
    public string? CurrencyCode { get; init; }

    /// <summary>Period the bank says this statement covers.</summary>
    public DateOnly? PeriodStart { get; init; }

    public DateOnly? PeriodEnd { get; init; }

    public required IReadOnlyList<OfxTransaction> Transactions { get; init; }

    /// <summary>Closing balance the bank states. The cross-check for the amounts' sign.</summary>
    public OfxBalance? LedgerBalance { get; init; }

    public OfxBalance? AvailableBalance { get; init; }

    /// <summary>Institution name from the sign-on response, e.g. "Contoso Bank".</summary>
    public string? Institution { get; init; }

    /// <summary>Earliest posted date actually present, whatever the stated period says.</summary>
    public DateOnly? FirstPosted =>
        Transactions.Count == 0 ? null : Transactions.Min(t => t.Posted);

    public DateOnly? LastPosted =>
        Transactions.Count == 0 ? null : Transactions.Max(t => t.Posted);

    /// <summary>Sum of every transaction as stated, before any sign correction.</summary>
    public Money Total => Money.Sum(Transactions.Select(t => t.Amount));
}

/// <summary>Everything read from one file.</summary>
public sealed record OfxParseResult
{
    public required OfxHeader Header { get; init; }

    public required IReadOnlyList<OfxStatement> Statements { get; init; }

    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }

    /// <summary>True when nothing can be imported and the user must be told why.</summary>
    public bool HasBlockingError => Diagnostics.Any(d => d.Severity == ImportSeverity.Error);

    public IEnumerable<ImportDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == ImportSeverity.Error);

    public IEnumerable<ImportDiagnostic> Warnings =>
        Diagnostics.Where(d => d.Severity == ImportSeverity.Warning);
}
