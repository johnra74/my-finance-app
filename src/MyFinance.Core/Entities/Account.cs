using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Entities;

/// <summary>A single financial account: one register, one running balance.</summary>
public class Account
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public AccountType Type { get; set; }

    /// <summary>Bank or issuer name, e.g. "Contoso Bank".</summary>
    public string? Institution { get; set; }

    /// <summary>
    /// Masked account number for display only, e.g. "XXXXXXXX6464". Full numbers are never
    /// stored — nothing in the app needs one, and holding it would only add risk.
    /// </summary>
    public string? AccountNumberMasked { get; set; }

    /// <summary>
    /// Balance carried into the first recorded transaction. Register balances are this plus
    /// the running sum of transaction amounts.
    /// </summary>
    public Money OpeningBalance { get; set; }

    public DateOnly? OpenedOn { get; set; }

    /// <summary>ISO 4217 code. v1 is single-currency; this exists so amounts are labelled.</summary>
    public string CurrencyCode { get; set; } = "USD";

    /// <summary>Closed accounts stay in the books for history but drop out of pickers.</summary>
    public bool IsClosed { get; set; }

    /// <summary>Shown on the Home dashboard's favourite-accounts tile.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Manual ordering within its group in the account list.</summary>
    public int SortOrder { get; set; }

    public DateOnly? LastReconciledOn { get; set; }

    /// <summary>Statement balance agreed at the last completed reconciliation.</summary>
    public Money LastReconciledBalance { get; set; }

    /// <summary>When a download was last imported into this account.</summary>
    public DateTimeOffset? LastUpdatedOn { get; set; }

    /// <summary>
    /// Keyed digest of the bank and account identifiers carried by an OFX statement, used
    /// to recognise which account a downloaded file belongs to.
    /// </summary>
    /// <remarks>
    /// A digest rather than the identifiers themselves, for the same reason only a masked
    /// account number is kept: nothing here needs the real number, and holding it would
    /// only add risk. It is an HMAC under a per-book secret rather than a plain hash —
    /// account numbers carry far too little entropy to hash safely, and a digest that can
    /// be reversed by enumeration would defeat the point of not storing the number.
    /// </remarks>
    public string? OfxAccountKey { get; set; }

    /// <summary>
    /// The routing or issuer id from the statement. Not secret on its own — it identifies
    /// the institution, not the account — and useful for showing why a file matched.
    /// </summary>
    public string? OfxBankId { get; set; }

    /// <summary>Free-text note shown on the account details screen.</summary>
    public string? Notes { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];

    /// <summary>The heading this account is subtotalled under in the account list.</summary>
    public AccountGroup Group => Type switch
    {
        AccountType.Checking or AccountType.Savings or AccountType.MoneyMarket
            or AccountType.CertificateOfDeposit or AccountType.Cash => AccountGroup.Bank,
        AccountType.CreditCard or AccountType.LineOfCredit => AccountGroup.Credit,
        _ => AccountGroup.Other,
    };

    /// <summary>
    /// True when a positive balance means money owed rather than money held. Reports negate
    /// these so a credit card's debt reads as a positive liability on the net-worth report.
    /// </summary>
    public bool IsLiability => Group == AccountGroup.Credit;

    /// <summary>
    /// Imported-but-unmodelled accounts (loans, brokerages) are read-only and excluded from
    /// spending analysis rather than silently dropped at migration time.
    /// </summary>
    public bool IsReadOnly => Type == AccountType.UnsupportedImported;
}
