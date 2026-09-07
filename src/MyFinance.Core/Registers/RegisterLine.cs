using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Registers;

/// <summary>
/// A transaction paired with the account balance as at that row.
/// </summary>
/// <remarks>
/// The running balance is never stored. It is a property of a transaction's position in an
/// ordered sequence, so persisting it would create a second source of truth that any edit,
/// import or back-dated entry could silently invalidate.
/// </remarks>
public sealed record RegisterLine
{
    public required Transaction Transaction { get; init; }

    /// <summary>Account balance after applying this transaction.</summary>
    public required Money Balance { get; init; }

    /// <summary>Zero-based position in the ordered register.</summary>
    public required int Index { get; init; }

    /// <summary>Money out, as a positive number, for the register's Payment column.</summary>
    public Money? Payment =>
        Transaction.Amount.IsNegative ? Transaction.Amount.Abs() : null;

    /// <summary>Money in, as a positive number, for the register's Deposit column.</summary>
    public Money? Deposit =>
        Transaction.Amount.IsPositive ? Transaction.Amount : null;
}
