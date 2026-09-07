using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Validation;

/// <summary>A single reason a transaction is not valid.</summary>
/// <param name="Code">Stable identifier for tests and for mapping to UI messages.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record ValidationError(string Code, string Message);

/// <summary>The outcome of validating something, with all failures rather than just the first.</summary>
public sealed record ValidationResult(IReadOnlyList<ValidationError> Errors)
{
    public static ValidationResult Success { get; } = new([]);

    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Fail(params ValidationError[] errors) => new(errors);
}

/// <summary>
/// Enforces the invariants that keep the books internally consistent.
/// </summary>
/// <remarks>
/// These are checked centrally rather than in the UI, because transactions arrive from three
/// directions — the register, file import, and the scheduled-bill engine — and an invariant
/// enforced in only one of them is not an invariant.
/// </remarks>
public static class TransactionValidator
{
    public const string SplitsMissing = "splits.missing";
    public const string SplitsDoNotSumToTotal = "splits.sum_mismatch";
    public const string TransferMustHavePeer = "transfer.peer_missing";
    public const string TransferPeerMismatch = "transfer.peer_mismatch";
    public const string TransferSameAccount = "transfer.same_account";
    public const string DateOutOfRange = "date.out_of_range";
    public const string AccountMissing = "account.missing";

    /// <summary>Earliest date accepted; guards against a mis-parsed import year.</summary>
    public static readonly DateOnly MinimumDate = new(1900, 1, 1);

    public static ValidationResult Validate(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var errors = new List<ValidationError>();

        if (transaction.AccountId == 0 && transaction.Account is null)
        {
            errors.Add(new ValidationError(AccountMissing, "A transaction must belong to an account."));
        }

        if (transaction.Date < MinimumDate)
        {
            errors.Add(new ValidationError(
                DateOutOfRange,
                $"Date {transaction.Date:yyyy-MM-dd} is before {MinimumDate:yyyy-MM-dd}, which usually means a misread import."));
        }

        // Every transaction carries at least one split, even when uncategorized, so that
        // reports can aggregate over splits alone without a fallback path.
        if (transaction.Splits.Count == 0)
        {
            errors.Add(new ValidationError(
                SplitsMissing,
                "A transaction must have at least one split, even if it is uncategorized."));
        }
        else
        {
            Money splitTotal = Money.Sum(transaction.Splits.Select(s => s.Amount));
            if (splitTotal != transaction.Amount)
            {
                errors.Add(new ValidationError(
                    SplitsDoNotSumToTotal,
                    $"Splits total {splitTotal.ToAccountingString()} but the transaction is {transaction.Amount.ToAccountingString()}."));
            }
        }

        return errors.Count == 0 ? ValidationResult.Success : new ValidationResult(errors);
    }

    /// <summary>
    /// Validates both legs of a transfer together. The two rows must sit in different
    /// accounts, point at each other, and cancel out exactly — otherwise moving money between
    /// your own accounts would create or destroy value in the net-worth report.
    /// </summary>
    /// <remarks>
    /// <b>Cancellation is checked within a currency only.</b> Across two it cannot be: $100
    /// leaves one account and £78 arrives in the other, exactly as the two statements state
    /// them, and this application holds no rate with which to check that pairing. Such a pair
    /// must still name each other and share a date; what is not asserted is the arithmetic,
    /// because there is none to assert. This is a deliberate weakening of the invariant in
    /// `002-accounts-and-register` FR-026 — see `specs/015-multi-currency` — and the
    /// same-currency case is exactly as strict as it has always been.
    /// </remarks>
    public static ValidationResult ValidateTransferPair(Transaction near, Transaction far)
    {
        ArgumentNullException.ThrowIfNull(near);
        ArgumentNullException.ThrowIfNull(far);

        var errors = new List<ValidationError>();

        if (near.AccountId == far.AccountId)
        {
            errors.Add(new ValidationError(
                TransferSameAccount,
                "A transfer must move money between two different accounts."));
        }

        if (near.Amount.Currency == far.Amount.Currency)
        {
            if (near.Amount != far.Amount.Negated())
            {
                errors.Add(new ValidationError(
                    TransferPeerMismatch,
                    $"Transfer legs must cancel out, but {near.Amount.ToAccountingString()} and {far.Amount.ToAccountingString()} do not."));
            }
        }
        else if (near.Amount.Sign == far.Amount.Sign && !near.Amount.IsZero)
        {
            // The one thing still checkable across currencies: money left one account, so it
            // must have arrived in the other. Two legs pointing the same way is a transfer
            // that creates or destroys value whatever the rate.
            errors.Add(new ValidationError(
                TransferPeerMismatch,
                "One leg of a transfer must be money out and the other money in."));
        }

        if (near.Date != far.Date)
        {
            errors.Add(new ValidationError(
                TransferPeerMismatch,
                "Both legs of a transfer must share the same date."));
        }

        return errors.Count == 0 ? ValidationResult.Success : new ValidationResult(errors);
    }

    /// <summary>
    /// Checks that a transaction flagged as a transfer actually references a peer, catching
    /// half-written transfers left behind by a failed edit.
    /// </summary>
    public static ValidationResult ValidateTransferLinkage(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction.IsTransfer && transaction.TransferPeerId == transaction.Id)
        {
            return ValidationResult.Fail(new ValidationError(
                TransferSameAccount,
                "A transfer cannot point at itself."));
        }

        return ValidationResult.Success;
    }
}
