namespace MyFinance.Core.Registers;

using MyFinance.Core.Primitives;

/// <summary>
/// Maps transaction entry mode (payment/deposit/transfer) and user input to a normalized
/// signed Money amount and category/transfer fields, per spec 002 FR-018 and FR-026–029.
/// </summary>
public static class EntryModeMapper
{
    /// <summary>
    /// Maps entry mode and an unsigned (positive) entered amount to a signed Money amount.
    /// Payment: returns negative (money leaving). Deposit/Transfer: returns positive.
    /// </summary>
    public static Money MapAmount(EntryMode mode, Money enteredAmount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(enteredAmount.MinorUnits);

        return mode switch
        {
            EntryMode.Payment => Money.FromMinorUnits(-enteredAmount.MinorUnits),
            EntryMode.Deposit => enteredAmount,
            EntryMode.Transfer => Money.FromMinorUnits(-enteredAmount.MinorUnits),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    /// <summary>
    /// Returns whether a category is permitted in the given mode.
    /// Payment and Deposit: yes. Transfer: no (transfers have no category).
    /// </summary>
    public static bool IsCategoryAllowed(EntryMode mode) => mode is not EntryMode.Transfer;

    /// <summary>
    /// Returns whether a transfer target account is required/permitted in the given mode.
    /// Transfer: required. Payment/Deposit: not permitted.
    /// </summary>
    public static bool IsTransferTargetAllowed(EntryMode mode) => mode == EntryMode.Transfer;

    /// <summary>
    /// Normalizes category ID based on mode.
    /// Transfer mode: always returns null (transfers have no category).
    /// Other modes: returns the input categoryId unchanged.
    /// </summary>
    public static int? NormalizeCategory(EntryMode mode, int? categoryId)
        => mode == EntryMode.Transfer ? null : categoryId;

    /// <summary>
    /// Validates that transfer target is present/absent as appropriate for the mode.
    /// Transfer: requires non-null transferAccountId.
    /// Other modes: requires null transferAccountId.
    /// Returns true if valid; false otherwise.
    /// </summary>
    public static bool ValidateTransferTarget(EntryMode mode, int? transferAccountId)
    {
        if (mode == EntryMode.Transfer)
            return transferAccountId is not null;
        else
            return transferAccountId is null;
    }

    /// <summary>
    /// Validates that a transfer target is not the same account as the source.
    /// (Calling code must provide the source account ID; this validates the rule
    /// "refuse a transfer whose two ends are the same account" per spec 002 FR-028.)
    /// </summary>
    public static bool ValidateTransferNotSameAccount(int sourceAccountId, int? targetAccountId)
    {
        if (targetAccountId is null)
            return true; // Not a transfer; rule does not apply.
        return sourceAccountId != targetAccountId;
    }
}
