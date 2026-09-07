using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Entities;

/// <summary>Something that can be held: a share, a fund, a bond.</summary>
/// <remarks>
/// Identity is the security, not the holding. The same fund held in two accounts is one
/// <see cref="Security"/> and two <see cref="Holding"/> rows, so a price entered once values
/// both.
/// </remarks>
public class Security
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Ticker or other short code, where there is one.</summary>
    public string? Symbol { get; set; }

    public SecurityType Type { get; set; }

    public string? Notes { get; set; }

    public ICollection<SecurityPrice> Prices { get; set; } = [];
}

/// <summary>
/// What is held of one security in one account, and what it cost.
/// </summary>
/// <remarks>
/// <para>
/// <b>Average cost, not individual lots.</b> <see cref="CostBasis"/> is the cost of the whole
/// holding; a sale removes a proportional share of it. That is enough to answer "what do I
/// hold" and "what did I pay", and not enough for capital gains — which need lot matching, a
/// policy the user would have to choose per sale, and a tax report this application does not
/// have. If one is ever specified, it brings lots with it.
/// </para>
/// <para>
/// A holding sold to zero is kept at zero rather than deleted, so its history survives — the
/// same reasoning that keeps closed accounts and archived categories.
/// </para>
/// </remarks>
public class Holding
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public Account? Account { get; set; }

    public int SecurityId { get; set; }

    public Security? Security { get; set; }

    /// <summary>Units held. Exact — see <see cref="Quantity"/>.</summary>
    public Quantity Quantity { get; set; }

    /// <summary>What the whole holding cost, averaged rather than tracked per lot.</summary>
    public Money CostBasis { get; set; }

    /// <summary>Cost of one unit, for display. Zero when nothing is held.</summary>
    public Money AverageCost => Quantity.IsZero
        ? Money.Zero
        : CostBasis * (1m / Quantity.ToDecimal());
}

/// <summary>
/// One thing that happened in an investment account.
/// </summary>
/// <remarks>
/// The cash side is an ordinary <see cref="Transaction"/> written through the normal register
/// path, linked here by <see cref="CashTransactionId"/>. That is what keeps this from becoming
/// a second ledger with its own balance rules: only the holding side is new.
/// </remarks>
public class InvestmentTransaction
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public Account? Account { get; set; }

    public int SecurityId { get; set; }

    public Security? Security { get; set; }

    public DateOnly Date { get; set; }

    public InvestmentActivity Activity { get; set; }

    /// <summary>Units bought or sold. Zero for a cash dividend or a fee.</summary>
    public Quantity Quantity { get; set; }

    /// <summary>Price per unit at the time. Zero where it does not apply.</summary>
    public Money PricePerUnit { get; set; }

    /// <summary>Total cash moved, signed from the account's point of view.</summary>
    public Money Amount { get; set; }

    public Money Fees { get; set; }

    public string? Memo { get; set; }

    /// <summary>The register row this produced. Null only for a holding correction.</summary>
    public int? CashTransactionId { get; set; }

    public Transaction? CashTransaction { get; set; }
}

/// <summary>
/// What a security was worth on a day.
/// </summary>
/// <remarks>
/// <see cref="AsOf"/> is not metadata. Prices are entered by hand — constitution 7 rules out
/// fetching them — so a valuation is only as current as the last time somebody typed one, and
/// every figure derived from a price is shown with that price's date beside it. A number whose
/// basis the reader cannot see is one they have to check from scratch.
/// </remarks>
public class SecurityPrice
{
    public int Id { get; set; }

    public int SecurityId { get; set; }

    public Security? Security { get; set; }

    /// <summary>The day this price applies to.</summary>
    public DateOnly AsOf { get; set; }

    public Money Price { get; set; }
}
