namespace MyFinance.Core.Enums;

/// <summary>The kind of account being tracked. v1 covers deposit and credit accounts.</summary>
public enum AccountType
{
    Checking = 0,
    Savings = 1,
    MoneyMarket = 2,
    CertificateOfDeposit = 3,
    Cash = 4,
    CreditCard = 5,
    LineOfCredit = 6,

    /// <summary>Holds securities as well as cash. See `specs/013-investment-accounts`.</summary>
    Brokerage = 7,

    /// <summary>
    /// A loan or mortgage carried over from a Microsoft Money file that this version does not
    /// fully model. Balance-only: excluded from spending reports and not editable in the
    /// register.
    /// </summary>
    /// <remarks>
    /// Investment accounts used to land here too and no longer do — they are
    /// <see cref="Brokerage"/>. Loans and mortgages stay, because amortization is its own gap.
    /// The value is 99 rather than 8 so real types can be added without renumbering anything
    /// already on disk.
    /// </remarks>
    UnsupportedImported = 99,
}

/// <summary>What kind of thing a security is.</summary>
public enum SecurityType
{
    Share = 0,
    Fund = 1,
    Bond = 2,
    Other = 3,
}

/// <summary>What happened in an investment account.</summary>
public enum InvestmentActivity
{
    Buy = 0,
    Sell = 1,

    /// <summary>Cash paid out. Reaches the register like any other deposit.</summary>
    Dividend = 2,

    /// <summary>A dividend used to buy more units instead of paying out.</summary>
    Reinvestment = 3,

    Fee = 4,
}

/// <summary>
/// The heading an account appears under in the account list, mirroring how Microsoft Money
/// groups and subtotals them.
/// </summary>
public enum AccountGroup
{
    Bank = 0,
    Credit = 1,
    Other = 2,
}

/// <summary>Reconciliation state of a single transaction.</summary>
public enum ClearedStatus
{
    /// <summary>Entered but not yet seen on a statement.</summary>
    Uncleared = 0,

    /// <summary>Seen on a statement or an imported download, but not yet reconciled.</summary>
    Cleared = 1,

    /// <summary>Locked in by a completed reconciliation.</summary>
    Reconciled = 2,
}

/// <summary>Whether a category represents money coming in or going out.</summary>
public enum CategoryKind
{
    Expense = 0,
    Income = 1,
}

/// <summary>How often a scheduled transaction repeats.</summary>
public enum RecurrenceFrequency
{
    Once = 0,
    Daily = 1,
    Weekly = 2,
    EveryTwoWeeks = 3,
    TwiceAMonth = 4,
    EveryFourWeeks = 5,
    Monthly = 6,
    EveryTwoMonths = 7,
    Quarterly = 8,
    TwiceAYear = 9,
    Yearly = 10,
}

/// <summary>What to do when a computed due date lands on a weekend or holiday.</summary>
public enum WeekendShift
{
    None = 0,
    PreviousBusinessDay = 1,
    NextBusinessDay = 2,
}

/// <summary>How a scheduled bill is paid, shown in the bills summary.</summary>
public enum PaymentMethod
{
    Unspecified = 0,
    WriteCheck = 1,
    DirectDebit = 2,
    DirectDeposit = 3,
    ElectronicPayment = 4,
    Cash = 5,
    CreditCard = 6,
    Transfer = 7,
}

/// <summary>Progress of one occurrence of a scheduled transaction.</summary>
public enum ScheduleOccurrenceState
{
    Pending = 0,
    Entered = 1,
    Skipped = 2,
}

/// <summary>How a scheduled series ends.</summary>
public enum RecurrenceEndKind
{
    Never = 0,
    OnDate = 1,
    AfterOccurrences = 2,
}

/// <summary>The transaction field a categorization rule inspects.</summary>
public enum RuleMatchField
{
    Payee = 0,
    Memo = 1,
    PayeeOrMemo = 2,
    Amount = 3,
}

/// <summary>How a categorization rule compares its pattern against the field.</summary>
public enum RuleMatchKind
{
    Contains = 0,
    Equals = 1,
    StartsWith = 2,
    EndsWith = 3,
    Regex = 4,
}

/// <summary>Source format of an import batch.</summary>
public enum ImportFormat
{
    Ofx = 0,
    Qif = 1,
    MicrosoftMoney = 2,
    Manual = 3,
}

/// <summary>The period a budget line covers.</summary>
public enum BudgetPeriodType
{
    Monthly = 0,
    Annual = 1,
}
