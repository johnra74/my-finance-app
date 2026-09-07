using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Data.Services;

/// <summary>
/// The editable shape of an account, as the account editor collects it.
/// </summary>
/// <remarks>
/// Drafts are deliberately separate from the entities. The editor binds to a draft, so a
/// cancelled edit leaves nothing half-applied to a tracked entity, and the service decides
/// which fields an edit is allowed to move.
/// </remarks>
public sealed class AccountDraft
{
    /// <summary>Null when creating.</summary>
    public int? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public AccountType Type { get; set; } = AccountType.Checking;

    public string? Institution { get; set; }

    public string? AccountNumberMasked { get; set; }

    public Money OpeningBalance { get; set; }

    public DateOnly? OpenedOn { get; set; }

    public string CurrencyCode { get; set; } = "USD";

    public bool IsClosed { get; set; }

    public bool IsFavorite { get; set; }

    public int SortOrder { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Set by the importer so the next statement from the same bank account recognises it.
    /// Left null by the account editor, which has no statement to derive it from.
    /// </summary>
    public string? OfxAccountKey { get; set; }

    public string? OfxBankId { get; set; }
}

/// <summary>One category allocation being written as part of a transaction.</summary>
public sealed class SplitDraft
{
    public int? CategoryId { get; set; }

    public Money Amount { get; set; }

    public string? Memo { get; set; }
}

/// <summary>
/// The editable shape of a register row.
/// </summary>
/// <remarks>
/// <see cref="PayeeName"/> is a name rather than an id because the register lets you type a
/// payee that does not exist yet; the service creates it. <see cref="TransferAccountId"/>
/// being set is what makes this a transfer, and the service maintains the matching row in
/// the other account.
/// </remarks>
public sealed class TransactionDraft
{
    /// <summary>Null when creating.</summary>
    public int? Id { get; set; }

    public int AccountId { get; set; }

    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public string? Number { get; set; }

    /// <summary>Free text. Resolved to an existing payee or used to create one.</summary>
    public string? PayeeName { get; set; }

    public string? Memo { get; set; }

    /// <summary>Signed from the owning account's point of view: negative is money leaving.</summary>
    public Money Amount { get; set; }

    public ClearedStatus ClearedStatus { get; set; }

    public bool IsVoid { get; set; }

    /// <summary>Set to make this a transfer into that account. Null for an ordinary entry.</summary>
    public int? TransferAccountId { get; set; }

    /// <summary>
    /// Category allocation. Empty means uncategorized — the service still writes one split,
    /// because every report aggregates over splits and a transaction without one would
    /// silently vanish from them.
    /// </summary>
    public IReadOnlyList<SplitDraft> Splits { get; set; } = [];

    public bool IsTransfer => TransferAccountId is not null;
}
