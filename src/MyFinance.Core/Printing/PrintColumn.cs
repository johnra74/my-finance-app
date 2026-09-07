namespace MyFinance.Core.Printing;

/// <summary>
/// One column a printed table can carry.
/// </summary>
/// <remarks>
/// Widths are in device-independent pixels — 1/96 inch, which is what WPF's print dialog
/// reports for the printable area, so the arithmetic here and the geometry on the page are in
/// the same units without a conversion nobody would remember to apply.
/// </remarks>
/// <param name="Key">Stable identifier, used to remember the user's choice between sessions.</param>
/// <param name="Header">What the column is called at the top of every page.</param>
/// <param name="Width">Width in device-independent pixels.</param>
/// <param name="Priority">
/// Drop order when the page is too narrow: the highest number goes first. A column the user
/// asked for is never dropped whatever its priority — see <see cref="ColumnFitter"/>.
/// </param>
/// <param name="IsNumeric">Right-aligned, and the reason a money column is never truncated.</param>
public sealed record PrintColumn(
    string Key,
    string Header,
    double Width,
    int Priority,
    bool IsNumeric = false);

/// <summary>
/// The register's columns, in the order they print.
/// </summary>
/// <remarks>
/// The priorities are a judgement about what a register is <em>for</em>. A date, what left the
/// account, what arrived and the running balance are the reason somebody prints one at all, so
/// they are last to go. A memo is the first: it is the widest column and the one whose absence
/// is most obvious, which is exactly why it must never disappear without being asked to.
/// </remarks>
public static class RegisterColumns
{
    public static readonly PrintColumn Date = new("date", "Date", 70, 0);
    public static readonly PrintColumn Payment = new("payment", "Payment", 80, 1, IsNumeric: true);
    public static readonly PrintColumn Deposit = new("deposit", "Deposit", 80, 2, IsNumeric: true);
    public static readonly PrintColumn Balance = new("balance", "Balance", 90, 3, IsNumeric: true);
    public static readonly PrintColumn Payee = new("payee", "Payee", 160, 4);
    public static readonly PrintColumn Category = new("category", "Category", 140, 5);
    public static readonly PrintColumn Cleared = new("cleared", "C", 24, 6);
    public static readonly PrintColumn Number = new("number", "Number", 50, 7);
    public static readonly PrintColumn Memo = new("memo", "Memo", 160, 8);

    /// <summary>Every column the register can print, in printing order.</summary>
    public static IReadOnlyList<PrintColumn> All { get; } =
        [Date, Number, Payee, Category, Memo, Cleared, Payment, Deposit, Balance];

    public static PrintColumn? ByKey(string key) =>
        All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
}

/// <summary>
/// The columns a printed report table carries.
/// </summary>
/// <remarks>
/// Narrower than a register's, and all four fit any page, so nothing is ever dropped here.
/// The share column is what makes the table readable without the chart — which is the point
/// of printing both.
/// </remarks>
public static class ReportColumns
{
    public static readonly PrintColumn Label = new("label", "Category", 260, 0);
    public static readonly PrintColumn Amount = new("amount", "Amount", 120, 1, IsNumeric: true);
    public static readonly PrintColumn Share = new("share", "Share", 80, 2, IsNumeric: true);
    public static readonly PrintColumn Count = new("count", "Items", 70, 3, IsNumeric: true);

    public static IReadOnlyList<PrintColumn> All { get; } = [Label, Amount, Share, Count];
}
