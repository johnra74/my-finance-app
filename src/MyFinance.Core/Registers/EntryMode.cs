namespace MyFinance.Core.Registers;

/// <summary>
/// The direction of a transaction entry: payment (money leaving), deposit (money entering),
/// or transfer (moving between accounts).
/// </summary>
public enum EntryMode
{
    Payment,
    Deposit,
    Transfer,
}
