using MyFinance.Import.Mny.Jet;

namespace MyFinance.Import.Mny;

/// <summary>
/// Finds Money's tables by the columns they declare.
/// </summary>
/// <remarks>
/// <para>
/// Money encrypts the catalog that holds table names, so a table has to be recognised by its
/// shape. That turns out to be sturdier than a name would be: Money's column names have been
/// stable across every version since 2002, and a signature that stops matching is a loud
/// failure rather than a table quietly read as the wrong thing.
/// </para>
/// <para>
/// Each signature names columns that together occur in exactly one table. Several tables
/// carry <c>hacct</c>, so matching on that alone would pick up the statement and online-service
/// tables as well; the extra columns are what make each one unambiguous.
/// </para>
/// </remarks>
public static class MoneyTables
{
    /// <summary>Accounts. <c>amtOpen</c> and <c>fClosed</c> separate it from the balance cache.</summary>
    public static readonly string[] AccountSignature = ["hacct", "szFull", "at", "amtOpen", "fClosed"];

    /// <summary>Transactions.</summary>
    public static readonly string[] TransactionSignature = ["htrn", "hacct", "dt", "amt", "grftt", "lHpay"];

    /// <summary>Payees. <c>hpayParent</c> keeps it apart from every other <c>szFull</c> table.</summary>
    public static readonly string[] PayeeSignature = ["hpay", "hpayParent", "szFull"];

    /// <summary>Categories.</summary>
    public static readonly string[] CategorySignature = ["hcat", "hcatParent", "nLevel", "szFull"];

    /// <summary>Which transactions are the parts of a split.</summary>
    public static readonly string[] SplitSignature = ["htrn", "htrnParent", "iSplit"];

    /// <summary>Which two transactions form a transfer.</summary>
    public static readonly string[] TransferLinkSignature = ["htrnFrom", "htrnLink"];

    /// <summary>
    /// Money's curated map from a merchant's industry code to a category.
    /// </summary>
    /// <remarks>
    /// A two-column table and the only one carrying both of these, which is what makes the
    /// signature safe: several tables have an <c>hcat</c>, and the securities table has a
    /// <c>sic</c>, but nothing else has the pair.
    /// </remarks>
    public static readonly string[] MerchantCodeSignature = ["sic", "hcat"];

    /// <summary>
    /// Money's recurring bills and deposits — the rows on its Bills summary screen.
    /// </summary>
    /// <remarks>
    /// <c>frq</c> and <c>cFrqInst</c> alone would not do: budgets, accounts and even securities
    /// carry the same pair. <c>hbill</c> with <c>hbillHead</c> and <c>cDaysAutoEnter</c> is what
    /// makes this table the only one that matches.
    /// </remarks>
    /// <summary>Securities: what can be held.</summary>
    public static readonly string[] SecuritySignature = ["hsec", "szFull", "szSymbol", "sct"];

    /// <summary>Positions: how much of a security an account holds.</summary>
    public static readonly string[] HoldingSignature = ["hsoq", "hsec", "hacct", "dQty"];

    /// <summary>Recorded prices, one per security per day.</summary>
    public static readonly string[] SecurityPriceSignature = ["hsp", "hsec", "dt", "dPrice"];

    public static readonly string[] ScheduledSignature =
        ["hbill", "hbillHead", "frq", "cFrqInst", "cDaysAutoEnter"];

    /// <summary>Finds the one table matching a signature, or null when none does.</summary>
    public static JetTable? Find(JetDatabase database, params string[] signature)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(signature);

        JetTable? found = null;

        foreach (JetTable table in database.Tables)
        {
            if (!signature.All(table.Has))
            {
                continue;
            }

            // Two matches means the signature is no longer specific enough. Preferring the
            // bigger table would be a guess, and a guess here silently imports the wrong rows.
            if (found is not null)
            {
                return null;
            }

            found = table;
        }

        return found;
    }

    /// <summary>Finds a table or explains, in the user's terms, that the file is not readable.</summary>
    public static JetTable Require(JetDatabase database, string what, params string[] signature) =>
        Find(database, signature)
        ?? throw new JetException(
            $"The {what} table could not be found in this file. It may be a Money file from a version this reader does not know, or it may be damaged.");
}
