using MyFinance.Core.Enums;
using MyFinance.Import.Model;
using MyFinance.Core.Primitives;

namespace MyFinance.Import.Mny;

/// <summary>One account as Microsoft Money recorded it.</summary>
/// <param name="Id">Money's own <c>hacct</c>, kept so links can be resolved.</param>
/// <param name="Name">Account name.</param>
/// <param name="Type">Best reading of Money's account type.</param>
/// <param name="Group">Which side of the account list it belongs on.</param>
/// <param name="IsClosed">Money's closed flag.</param>
/// <param name="IsFavorite">Whether it was pinned to the home page.</param>
/// <param name="OpeningBalance">The balance the register starts from.</param>
/// <param name="OpenedOn">When the account was opened, when recorded.</param>
/// <param name="CreditLimit">Credit limit, for a card.</param>
/// <param name="AccountNumberMasked">Whatever tail Money held, never the full number.</param>
/// <param name="Institution">The bank's name, when Money knew it.</param>
public sealed record MoneyAccount(
    int Id,
    string Name,
    AccountType Type,
    AccountGroup Group,
    bool IsClosed,
    bool IsFavorite,
    Money OpeningBalance,
    DateOnly? OpenedOn,
    Money? CreditLimit,
    string? AccountNumberMasked,
    string? Institution);

/// <summary>One category, still in Money's three-level shape.</summary>
/// <param name="Id">Money's <c>hcat</c>.</param>
/// <param name="Name">Just this level's name, not the full path.</param>
/// <param name="ParentId">Money's <c>hcatParent</c>.</param>
/// <param name="Level">0 for the two roots, 1 for a heading, 2 for a subcategory.</param>
/// <param name="Kind">Which root it descends from.</param>
public sealed record MoneyCategory(
    int Id,
    string Name,
    int? ParentId,
    int Level,
    CategoryKind Kind);

/// <summary>One row of Money's merchant-code table: an industry code and its category.</summary>
/// <param name="Code">The SIC code.</param>
/// <param name="CategoryId">Money's <c>hcat</c> for it.</param>
public sealed record MoneyMerchantCode(string Code, int CategoryId);

/// <summary>A security Money knows about.</summary>
public sealed record MoneySecurity(int Id, string Name, string? Symbol);

/// <summary>
/// What an account holds of a security.
/// </summary>
/// <remarks>
/// <b>Money records no cost basis</b> that this reader can recover: its transaction table
/// carries no quantity, price or cost column, and investment activity is not written there in
/// a form that could be replayed. So a migrated holding brings its <em>quantity</em> across and
/// nothing about what it cost — see `specs/013-investment-accounts`.
/// </remarks>
public sealed record MoneyHolding(int AccountId, int SecurityId, decimal Quantity);

/// <summary>A price Money recorded for a security on a day.</summary>
public sealed record MoneySecurityPrice(int SecurityId, DateOnly AsOf, decimal Price);

/// <summary>One payee.</summary>
public sealed record MoneyPayee(int Id, string Name, bool IsHidden);

/// <summary>
/// One row of Money's transaction table.
/// </summary>
/// <remarks>
/// Money and this application already agree on the two things that matter most: an amount
/// is signed from the account's own point of view, and a transfer is two rows, one in each
/// account. That agreement is why the mapping below is a translation rather than a rebuild.
/// </remarks>
/// <param name="Id">Money's <c>htrn</c>.</param>
/// <param name="AccountId">The account the row sits in.</param>
/// <param name="LinkedAccountId">For a transfer, the account at the other end.</param>
/// <param name="Date">Date as entered.</param>
/// <param name="Amount">Signed from this account's point of view.</param>
/// <param name="CategoryId">Money's <c>hcat</c>, absent on transfers.</param>
/// <param name="PayeeId">Money's <c>lHpay</c>.</param>
/// <param name="Number">Cheque number or reference.</param>
/// <param name="Memo">Free text.</param>
/// <param name="Cleared">Reconciliation state.</param>
/// <param name="IsTransfer">Whether this row is one leg of a transfer.</param>
/// <param name="IsTransferSource">Whether it is the leg the money left.</param>
/// <param name="SplitParentId">Set on the parts of a split.</param>
/// <param name="SplitIndex">Position within the split.</param>
/// <param name="IsScheduledInstance">A bill Money projected but nobody entered.</param>
public sealed record MoneyTransaction(
    int Id,
    int AccountId,
    int? LinkedAccountId,
    DateOnly Date,
    Money Amount,
    int? CategoryId,
    int? PayeeId,
    string? Number,
    string? Memo,
    ClearedStatus Cleared,
    bool IsTransfer,
    bool IsTransferSource,
    int? SplitParentId,
    int SplitIndex,
    bool IsScheduledInstance,
    int? ScheduleHeadId = null)
{
    /// <summary>True when this row is one part of a split, not a transaction in itself.</summary>
    public bool IsSplitPart => SplitParentId is not null;
}

/// <summary>
/// Everything read out of a Money file, still in Money's own terms.
/// </summary>
/// <remarks>
/// Deliberately a faithful copy rather than a translation. Keeping the two steps apart means
/// the reading can be checked against Money's own screens before any of it is written into a
/// book, and a mapping decision that turns out wrong can be changed without reading the
/// 19 MB file again.
/// </remarks>
/// <summary>
/// One recurring bill or deposit, as Money records it.
/// </summary>
/// <param name="Id">Money's <c>hbill</c>.</param>
/// <param name="TemplateTransactionId">
/// Money's <c>lHtrn</c> — the template transaction, which carries the payee, amount, account
/// and category. Money keeps the recurrence on the bill row and everything else on that one.
/// <b>Not <c>hbillHead</c></b>: that is a series identifier stamped on every generated
/// instance, not a transaction id, and dereferencing it as one silently yields an unrelated
/// transaction for almost every bill.
/// </param>
/// <param name="AccountId">From the template row; the account the bill is paid from.</param>
/// <param name="PayeeId">From the template row.</param>
/// <param name="Amount">From the template row, signed as Money signs it.</param>
/// <param name="CategoryId">From the template row; null when the bill is uncategorized.</param>
/// <param name="Memo">From the template row.</param>
/// <param name="NextDue">Money's <c>dt</c> — the anchor the series is computed from.</param>
/// <param name="Frequency">
/// Null when Money's code has never been verified. <b>Not guessed</b> — an unmapped series is
/// reported for the user to set up rather than converted to something plausible.
/// </param>
/// <param name="EndDate">Null when the series never ends. Money writes 2200-12-31 for that.</param>
/// <param name="OccurrenceCount">Null when unlimited.</param>
/// <param name="DaysAheadToEnter">Money's <c>cDaysAutoEnter</c>; null when it does not auto-enter.</param>
/// <param name="RawFrequency">Money's <c>frq</c>, kept so an unmapped series can be reported precisely.</param>
/// <param name="RawCountPerPeriod">Money's <c>cFrqInst</c>, kept for the same reason.</param>
/// <param name="HeadId">
/// Money's <c>hbillHead</c> — the series key stamped on every instance this bill generated.
/// It is what lets an already-entered occurrence be recognised exactly, rather than guessed at
/// from a matching amount, which fails for every bill whose amount varies.
/// </param>
/// <param name="Revision">
/// Money's <c>iinst</c>: the instance number from which this row's terms take effect.
/// </param>
public sealed record MoneyScheduled(
    int Id,
    int TemplateTransactionId,
    int AccountId,
    int? PayeeId,
    Money Amount,
    int? CategoryId,
    string? Memo,
    DateOnly NextDue,
    RecurrenceFrequency? Frequency,
    DateOnly? EndDate,
    int? OccurrenceCount,
    int? DaysAheadToEnter,
    int? RawFrequency,
    double? RawCountPerPeriod,
    int? HeadId = null,
    int Revision = 0)
{
    /// <summary>False when Money's recurrence code has never been verified against a real book.</summary>
    public bool IsConvertible => Frequency is not null;
}

public sealed record MoneyBook
{
    public required IReadOnlyList<MoneyAccount> Accounts { get; init; }

    public required IReadOnlyList<MoneyCategory> Categories { get; init; }

    public required IReadOnlyList<MoneyPayee> Payees { get; init; }

    public required IReadOnlyList<MoneyTransaction> Transactions { get; init; }

    /// <summary>Money's merchant-code to category map, empty when the file has none.</summary>
    public IReadOnlyList<MoneyMerchantCode> MerchantCodes { get; init; } = [];

    /// <summary>Recurring bills and deposits, empty when the file has none.</summary>
    public IReadOnlyList<MoneyScheduled> Scheduled { get; init; } = [];

    /// <summary>Securities, holdings and prices. All empty when the file has no investments.</summary>
    public IReadOnlyList<MoneySecurity> Securities { get; init; } = [];

    public IReadOnlyList<MoneyHolding> Holdings { get; init; } = [];

    public IReadOnlyList<MoneySecurityPrice> SecurityPrices { get; init; } = [];

    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }

    /// <summary>Transactions that stand on their own, with split parts left out.</summary>
    public IEnumerable<MoneyTransaction> TopLevelTransactions =>
        Transactions.Where(t => !t.IsSplitPart);

    /// <summary>The parts of each split, by the transaction they belong to.</summary>
    public ILookup<int, MoneyTransaction> SplitPartsByParent =>
        Transactions.Where(t => t.SplitParentId is not null)
            .ToLookup(t => t.SplitParentId!.Value);

    /// <summary>
    /// The closing balance of one account, by Money's own reckoning.
    /// </summary>
    /// <remarks>
    /// Money counts scheduled instances it has projected into the register, which is why a
    /// book last touched years ago shows a balance far below its real one — the bills kept
    /// being projected and the salary did not. Matching that arithmetic exactly is what
    /// lets a migration be checked against Money's own account list.
    /// </remarks>
    public Money BalanceOf(int accountId)
    {
        MoneyAccount? account = Accounts.FirstOrDefault(a => a.Id == accountId);

        if (account is null)
        {
            return Money.Zero;
        }

        Money sum = Money.Sum(TopLevelTransactions
            .Where(t => t.AccountId == accountId)
            .Select(t => t.Amount));

        return account.OpeningBalance + sum;
    }
}
