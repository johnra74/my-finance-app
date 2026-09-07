using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Model;
using MyFinance.Import.Mny;

namespace MyFinance.Data.Services;

/// <summary>What the user can decide before a migration runs.</summary>
public sealed record MigrationOptions
{
    public static MigrationOptions Default { get; } = new();

    /// <summary>
    /// Whether accounts Money had closed come across.
    /// </summary>
    /// <remarks>
    /// On by default. A closed account still holds the history that explains where the money
    /// went, and dropping it silently changes every total that spans the years it was open.
    /// </remarks>
    public bool IncludeClosedAccounts { get; init; } = true;

    /// <summary>
    /// Whether to keep the bills Money projected into the register but nobody entered.
    /// </summary>
    /// <remarks>
    /// On by default, because leaving them out makes the migrated balances disagree with the
    /// figures on Money's own account list, and a migration you cannot check is one you
    /// cannot trust. Turning it off gives a tidier book that will not reconcile.
    /// </remarks>
    public bool IncludeScheduledInstances { get; init; } = true;

    /// <summary>Whether payees Money marked hidden are worth carrying over.</summary>
    public bool IncludePayeesWithNoTransactions { get; init; }
}

/// <summary>One account as it will arrive, for the preview.</summary>
public sealed record MigrationAccountPreview(
    int SourceId,
    string Name,
    AccountType Type,
    bool IsClosed,
    int TransactionCount,
    Money Balance);

/// <summary>What a Money file holds, before anything is written.</summary>
public sealed record MigrationPreview
{
    public required string FileName { get; init; }

    public required MoneyBook Book { get; init; }

    public required IReadOnlyList<MigrationAccountPreview> Accounts { get; init; }

    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }

    /// <summary>Whether the book being migrated into already holds anything.</summary>
    public required bool TargetBookIsEmpty { get; init; }

    public int CategoryCount => Book.Categories.Count(c => c.Level > 0);

    public int PayeeCount => Book.Payees.Count;

    public int TransactionCount => Accounts.Sum(a => a.TransactionCount);

    public int SplitCount => Book.Transactions.Count(t => t.IsSplitPart);

    public int ScheduledCount => Book.Transactions.Count(t => t.IsScheduledInstance);

    public Money Total => Money.Sum(Accounts.Select(a => a.Balance));

    public DateOnly? EarliestDate => Book.Transactions.Count == 0
        ? null
        : Book.Transactions.Min(t => t.Date);

    public DateOnly? LatestDate => Book.Transactions.Count == 0
        ? null
        : Book.Transactions.Max(t => t.Date);
}

/// <summary>What a completed migration did.</summary>
/// <summary>A recurring bill that was not converted, and why.</summary>
public sealed record UnconvertedBill(string PayeeName, Money Amount, string Reason);

public sealed record MigrationSummary
{
    public required int AccountsCreated { get; init; }

    public required int CategoriesCreated { get; init; }

    public required int PayeesCreated { get; init; }

    public required int TransactionsCreated { get; init; }

    public required int SplitsCreated { get; init; }

    public required int TransfersLinked { get; init; }

    /// <summary>Recurring bills converted into scheduled transactions.</summary>
    public int BillsCreated { get; init; }

    /// <summary>
    /// Positions brought across.
    /// </summary>
    /// <remarks>
    /// Quantity only. Money records no cost basis this reader can recover, so each arrives at
    /// a cost of zero and the diagnostics say so — see `specs/013-investment-accounts`.
    /// </remarks>
    public int HoldingsCreated { get; init; }

    /// <summary>
    /// Bills that were deliberately not converted, with the reason for each.
    /// </summary>
    /// <remarks>
    /// The user's manual list. Finite and complete is materially better than today, where the
    /// bills come across as nothing at all and they must find them for themselves.
    /// </remarks>
    public IReadOnlyList<UnconvertedBill> UnconvertedBills { get; init; } = [];

    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }

    /// <summary>
    /// The closing balance of every migrated account, for checking against Money.
    /// </summary>
    /// <remarks>
    /// The whole point of a migration is that nothing changed on the way across, and the
    /// only way to know that is to compare these against the figures Money shows. They are
    /// reported rather than merely computed so the user can do exactly that.
    /// </remarks>
    public required IReadOnlyList<MigrationAccountPreview> Accounts { get; init; }

    public Money Total => Money.Sum(Accounts.Select(a => a.Balance));
}
