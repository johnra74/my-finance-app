using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;

namespace MyFinance.Data.Services;

/// <summary>What the reconcile screen opens with.</summary>
public sealed record ReconcileSession
{
    public required Account Account { get; init; }

    /// <summary>
    /// Balance locked in by the last completed reconciliation. The statement's opening
    /// balance should equal this.
    /// </summary>
    public required Money StartingBalance { get; init; }

    public required DateOnly? LastReconciledOn { get; init; }

    /// <summary>Everything not yet reconciled, in register order — the items to tick off.</summary>
    public required IReadOnlyList<Transaction> Outstanding { get; init; }

    /// <summary>Ids that already carry the cleared flag, ticked when the screen opens.</summary>
    public IReadOnlyList<int> InitiallyCleared =>
        [.. Outstanding.Where(t => t.ClearedStatus == ClearedStatus.Cleared).Select(t => t.Id)];
}

/// <summary>The running arithmetic of a reconcile session, recomputed as items are ticked.</summary>
/// <param name="StartingBalance">Balance reconciled through last time.</param>
/// <param name="ClearedTotal">Sum of the items ticked in this session.</param>
/// <param name="StatementBalance">The ending balance printed on the statement.</param>
public readonly record struct ReconcileTally(
    Money StartingBalance,
    Money ClearedTotal,
    Money StatementBalance)
{
    /// <summary>Where the ticked items say the account stands.</summary>
    public Money ClearedBalance => StartingBalance + ClearedTotal;

    /// <summary>
    /// Statement minus ticked. Zero means the statement is reconciled; anything else is the
    /// amount still unaccounted for.
    /// </summary>
    public Money Difference => StatementBalance - ClearedBalance;

    public bool IsBalanced => Difference.IsZero;
}

/// <summary>Runs a reconciliation: agree a statement, then lock those items down.</summary>
public sealed class ReconcileService
{
    public const string NotBalanced = "reconcile.not_balanced";

    private readonly IBookContextFactory _factory;

    public ReconcileService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// Opens a session: the starting balance and every item the statement might contain.
    /// </summary>
    /// <remarks>
    /// Already-reconciled items are excluded. They are settled history, and offering them for
    /// re-ticking is how a reconciliation quietly double-counts.
    /// </remarks>
    public async Task<ReconcileSession> BeginAsync(
        int accountId,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        Account account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(
                RegisterService.AccountNotFound,
                "That account no longer exists.");

        List<Transaction> all = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Payee)
            .Where(t => t.AccountId == accountId && !t.IsVoid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Money starting = BalanceCalculator.ReconciledBalance(account.OpeningBalance, all);

        List<Transaction> outstanding =
        [
            .. BalanceCalculator.InRegisterOrder(
                all.Where(t => t.ClearedStatus != ClearedStatus.Reconciled))
        ];

        return new ReconcileSession
        {
            Account = account,
            StartingBalance = starting,
            LastReconciledOn = account.LastReconciledOn,
            Outstanding = outstanding,
        };
    }

    /// <summary>
    /// Locks in a completed reconciliation: ticked items become reconciled, the rest are
    /// marked uncleared, and the account records what it was agreed against.
    /// </summary>
    /// <remarks>
    /// Refuses to finish while the difference is non-zero. A reconciliation that does not
    /// balance has not established anything, and recording it would make the next one start
    /// from a figure the bank never agreed to.
    /// </remarks>
    public async Task CompleteAsync(
        int accountId,
        DateOnly statementDate,
        Money statementBalance,
        IReadOnlyList<int> clearedTransactionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clearedTransactionIds);

        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Account account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(
                RegisterService.AccountNotFound,
                "That account no longer exists.");

        List<Transaction> transactions = await db.Transactions
            .Where(t => t.AccountId == accountId && !t.IsVoid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ticked = clearedTransactionIds.ToHashSet();

        Money reconciledAlready = BalanceCalculator.ReconciledBalance(
            account.OpeningBalance,
            transactions);

        Money tickedTotal = Money.Sum(transactions
            .Where(t => ticked.Contains(t.Id) && t.ClearedStatus != ClearedStatus.Reconciled)
            .Select(t => t.Amount));

        var tally = new ReconcileTally(reconciledAlready, tickedTotal, statementBalance);

        if (!tally.IsBalanced)
        {
            throw new BookValidationException(
                NotBalanced,
                $"The statement is out by {tally.Difference.ToAccountingString()}. Tick or untick items until the difference is zero.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (Transaction transaction in transactions)
        {
            if (transaction.ClearedStatus == ClearedStatus.Reconciled)
            {
                continue;
            }

            ClearedStatus target = ticked.Contains(transaction.Id)
                ? ClearedStatus.Reconciled
                : ClearedStatus.Uncleared;

            if (transaction.ClearedStatus != target)
            {
                transaction.ClearedStatus = target;
                transaction.ModifiedUtc = now;
            }
        }

        account.LastReconciledOn = statementDate;
        account.LastReconciledBalance = statementBalance;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
