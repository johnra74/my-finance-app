using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Registers;

/// <summary>
/// Computes register orderings and the several different "balances" an account has.
/// </summary>
public static class BalanceCalculator
{
    /// <summary>
    /// The canonical register order: by date, then by the intra-day sequence, then by id.
    /// </summary>
    /// <remarks>
    /// The tiebreakers matter. Several transactions routinely share a date, and without a
    /// stable total order the running balance column would reshuffle between sessions purely
    /// on database row order — which looks like corrupted data to the user even though every
    /// individual figure is correct.
    /// </remarks>
    public static IOrderedEnumerable<Transaction> InRegisterOrder(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return transactions
            .OrderBy(t => t.Date)
            .ThenBy(t => t.SequenceInDay)
            .ThenBy(t => t.Id);
    }

    /// <summary>
    /// Builds the register: each transaction in order, carrying the balance as at that row.
    /// </summary>
    public static IReadOnlyList<RegisterLine> BuildRegister(
        Money openingBalance,
        IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var lines = new List<RegisterLine>();
        Money running = openingBalance;
        int index = 0;

        foreach (Transaction transaction in InRegisterOrder(transactions))
        {
            running += transaction.EffectiveAmount;
            lines.Add(new RegisterLine
            {
                Transaction = transaction,
                Balance = running,
                Index = index++,
            });
        }

        return lines;
    }

    /// <summary>
    /// Every transaction applied: the account's true position including items that have not
    /// yet reached the bank. Microsoft Money labels this the adjusted or ending balance.
    /// </summary>
    public static Money CurrentBalance(Money openingBalance, IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return openingBalance + Money.Sum(transactions.Select(t => t.EffectiveAmount));
    }

    /// <summary>
    /// Only what the bank has acknowledged — cleared and reconciled items. Money shows this
    /// as the bank balance, and it is the figure that should agree with online banking.
    /// </summary>
    public static Money ClearedBalance(Money openingBalance, IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return openingBalance + Money.Sum(transactions
            .Where(t => t.ClearedStatus is ClearedStatus.Cleared or ClearedStatus.Reconciled)
            .Select(t => t.EffectiveAmount));
    }

    /// <summary>
    /// Only items locked in by a completed reconciliation — the starting point the next
    /// reconciliation session builds on.
    /// </summary>
    public static Money ReconciledBalance(Money openingBalance, IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return openingBalance + Money.Sum(transactions
            .Where(t => t.ClearedStatus == ClearedStatus.Reconciled)
            .Select(t => t.EffectiveAmount));
    }

    /// <summary>
    /// Balance as at the end of <paramref name="asOf"/>, used by net-worth-over-time and by
    /// the cash-flow forecast.
    /// </summary>
    public static Money BalanceAsOf(
        Money openingBalance,
        IEnumerable<Transaction> transactions,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return openingBalance + Money.Sum(transactions
            .Where(t => t.Date <= asOf)
            .Select(t => t.EffectiveAmount));
    }
}
