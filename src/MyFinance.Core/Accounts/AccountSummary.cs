using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Accounts;

/// <summary>
/// One row of the account list: an account plus the balances the list displays.
/// </summary>
/// <remarks>
/// Both balances are shown because they answer different questions. The cleared balance is
/// what the bank thinks you have and is the figure that should agree with online banking;
/// the current balance also applies items that have not reached the bank yet, and is the
/// figure that tells you whether a cheque will bounce.
/// </remarks>
public sealed record AccountSummary
{
    public required Account Account { get; init; }

    /// <summary>Every non-void transaction applied. Microsoft Money calls this the adjusted balance.</summary>
    public required Money CurrentBalance { get; init; }

    /// <summary>Only cleared and reconciled items — the bank's view.</summary>
    public required Money ClearedBalance { get; init; }

    public required int TransactionCount { get; init; }

    /// <summary>Transactions with no category on any split, i.e. the triage backlog.</summary>
    public required int UncategorizedCount { get; init; }

    public int Id => Account.Id;

    public string Name => Account.Name;

    public AccountType Type => Account.Type;

    public AccountGroup Group => Account.Group;

    public bool IsClosed => Account.IsClosed;

    /// <summary>True when the two balances differ, i.e. something has not reached the bank.</summary>
    public bool HasPendingItems => CurrentBalance != ClearedBalance;
}

/// <summary>
/// A total, and what it is denominated in.
/// </summary>
/// <remarks>
/// The shape a total takes once a book may hold more than one currency. With one currency
/// there is exactly one of these and it reads exactly as a single figure always did.
/// </remarks>
public sealed record CurrencyTotal(Currency Currency, Money Amount)
{
    public string Text => Amount.ToAccountingString();
}

/// <summary>A heading in the account list with its own subtotal, e.g. "Bank accounts".</summary>
public sealed record AccountGroupSummary
{
    public required AccountGroup Group { get; init; }

    public required string Header { get; init; }

    public required IReadOnlyList<AccountSummary> Accounts { get; init; }

    /// <summary>
    /// The group's total.
    /// </summary>
    /// <remarks>
    /// Throws if the group somehow spans two currencies — there is no rate with which to add
    /// them, and a number that pretended otherwise would be worse than an error. Use
    /// <see cref="Subtotals"/> where that is possible; with one currency the two agree.
    /// </remarks>
    public Money Subtotal => Money.Sum(Accounts.Select(a => a.CurrentBalance));

    public Money ClearedSubtotal => Money.Sum(Accounts.Select(a => a.ClearedBalance));

    /// <summary>The group's totals, one per currency it holds.</summary>
    public IReadOnlyList<CurrencyTotal> Subtotals => CurrencyTotals.Of(Accounts, a => a.CurrentBalance);

    public int Count => Accounts.Count;
}

/// <summary>The whole account list: every group, and the grand total across them.</summary>
public sealed record AccountListSummary
{
    public static AccountListSummary Empty { get; } = new() { Groups = [] };

    public required IReadOnlyList<AccountGroupSummary> Groups { get; init; }

    /// <summary>
    /// Straight sum of every account balance. Credit balances are already negative when money
    /// is owed, so this is net worth without any special-casing per group.
    /// </summary>
    /// <remarks>
    /// Valid only while the book holds a single currency, which is every book today. Across
    /// two it throws rather than inventing a figure: this application holds no exchange rates,
    /// so there is nothing to convert with. <see cref="Totals"/> is the form that always works.
    /// </remarks>
    public Money Total => Money.Sum(Groups.Select(g => g.Subtotal));

    public Money ClearedTotal => Money.Sum(Groups.Select(g => g.ClearedSubtotal));

    /// <summary>
    /// Every account balance, totalled per currency.
    /// </summary>
    /// <remarks>
    /// One entry for a single-currency book, which then reads exactly as <see cref="Total"/>
    /// always did. More than one is what the account list shows instead of a grand total,
    /// because a grand total across currencies would be a number nobody could defend.
    /// </remarks>
    public IReadOnlyList<CurrencyTotal> Totals => CurrencyTotals.Of(AllAccounts, a => a.CurrentBalance);

    public IReadOnlyList<CurrencyTotal> ClearedTotals => CurrencyTotals.Of(AllAccounts, a => a.ClearedBalance);

    /// <summary>True when a single grand total is a meaningful thing to show.</summary>
    public bool IsSingleCurrency => Totals.Count <= 1;

    public int AccountCount => Groups.Sum(g => g.Count);

    public int UncategorizedCount => Groups.Sum(g => g.Accounts.Sum(a => a.UncategorizedCount));

    public IEnumerable<AccountSummary> AllAccounts => Groups.SelectMany(g => g.Accounts);
}
