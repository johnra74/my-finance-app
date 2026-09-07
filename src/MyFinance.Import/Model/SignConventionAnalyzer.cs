using MyFinance.Core.Primitives;
namespace MyFinance.Import.Model;

/// <summary>Whether a file's amounts should be taken as written or reversed.</summary>
public enum SignPolarity
{
    /// <summary>The file already signs amounts the way this application does.</summary>
    AsIs = 0,

    /// <summary>Every amount in the file needs its sign flipped.</summary>
    Inverted = 1,
}

/// <summary>How much the evidence supports the suggested polarity.</summary>
public enum SignConfidence
{
    /// <summary>Nothing decisive. Leave the file as written and say nothing alarming.</summary>
    Unknown = 0,

    /// <summary>Good evidence, but not proof. Tell the user; do not preselect a change.</summary>
    Likely = 1,

    /// <summary>The balances agree exactly under one reading and not the other.</summary>
    Certain = 2,
}

/// <summary>What the analyser concluded, and everything the banner needs to explain itself.</summary>
public sealed record SignVerdict
{
    public required SignPolarity Suggested { get; init; }

    public required SignConfidence Confidence { get; init; }

    /// <summary>Plain-English reasoning, shown to the user verbatim.</summary>
    public required string Explanation { get; init; }

    /// <summary>The closing balance the statement claims, if it gave one.</summary>
    public Money? LedgerBalance { get; init; }

    /// <summary>Where the account would land if the file is taken as written.</summary>
    public required Money ProjectedAsIs { get; init; }

    /// <summary>Where it would land with every sign flipped.</summary>
    public required Money ProjectedInverted { get; init; }

    /// <summary>Whether the wizard should tick the reverse-signs box for the user.</summary>
    public bool PreselectInversion =>
        Suggested == SignPolarity.Inverted && Confidence == SignConfidence.Certain;

    /// <summary>The balance the import will produce under the suggested reading.</summary>
    public Money Projected => Suggested == SignPolarity.Inverted ? ProjectedInverted : ProjectedAsIs;

    /// <summary>True when the projection agrees with what the bank says.</summary>
    public bool AgreesWithLedger => LedgerBalance is Money ledger && Projected == ledger;
}

/// <summary>
/// Decides whether a statement's amounts are signed the way this application expects.
/// </summary>
/// <remarks>
/// <para>
/// OFX signs amounts from the account holder's point of view, which matches the convention
/// here — money out is negative. Credit card issuers are genuinely inconsistent about it
/// though, and taking a whole statement backwards is the one import failure that quietly
/// corrupts a book rather than announcing itself.
/// </para>
/// <para>
/// Two independent signals are used. The balance cross-check is decisive when it applies;
/// the transaction types are the fallback, and they matter because the balance check tells
/// you nothing at all on a brand-new account, which is exactly when the first import happens.
/// </para>
/// </remarks>
public static class SignConventionAnalyzer
{
    /// <summary>Money leaving the account, so a negative amount is expected.</summary>
    private static readonly HashSet<string> MoneyOutTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "DEBIT", "PAYMENT", "FEE", "SRVCHG", "POS", "ATM", "CHECK",
            "DIRECTDEBIT", "REPEATPMT", "CASH",
        };

    /// <summary>Money arriving, so a positive amount is expected.</summary>
    private static readonly HashSet<string> MoneyInTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CREDIT", "DEP", "INT", "DIV", "DIRECTDEP",
        };

    /// <summary>Below this many classifiable rows, the type check abstains.</summary>
    public const int MinimumTypedRows = 5;

    /// <summary>
    /// Analyses a statement against the account's current position.
    /// </summary>
    /// <param name="rows">The transactions that would actually be imported.</param>
    /// <param name="ledgerBalance">The statement's stated closing balance, if any.</param>
    /// <param name="currentBalance">The account's balance before this import.</param>
    /// <param name="kind">Bank or credit card; bank statements are treated more cautiously.</param>
    public static SignVerdict Analyze(
        IReadOnlyList<ImportedTransaction> rows,
        Money? ledgerBalance,
        Money currentBalance,
        ImportedAccountKind kind)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Money total = Money.Sum(rows.Select(r => r.Amount));
        Money asIs = currentBalance + total;
        Money inverted = currentBalance - total;

        SignVerdict Build(SignPolarity polarity, SignConfidence confidence, string explanation) =>
            Guard(new SignVerdict
            {
                Suggested = polarity,
                Confidence = confidence,
                Explanation = explanation,
                LedgerBalance = ledgerBalance,
                ProjectedAsIs = asIs,
                ProjectedInverted = inverted,
            }, kind);

        if (rows.Count == 0)
        {
            return Build(SignPolarity.AsIs, SignConfidence.Unknown, "There is nothing to import.");
        }

        if (ledgerBalance is Money ledger)
        {
            bool asIsMatches = asIs == ledger;
            bool invertedMatches = inverted == ledger;

            // When the sum is zero both readings give the same answer, so the balance proves
            // nothing — as it also does on a new account with no opening balance.
            if (asIsMatches && !invertedMatches)
            {
                return Build(
                    SignPolarity.AsIs,
                    SignConfidence.Certain,
                    $"The amounts are the right way round: importing gives {asIs.ToAccountingString()}, which is exactly the balance the statement reports.");
            }

            if (invertedMatches && !asIsMatches)
            {
                return Build(
                    SignPolarity.Inverted,
                    SignConfidence.Certain,
                    $"The amounts in this file look reversed. The statement reports {ledger.ToAccountingString()}, which is what you get by flipping every sign; taking the file as written would give {asIs.ToAccountingString()}.");
            }

            // The magnitudes can agree while the signs do not, because issuers also disagree
            // about which way round the stated balance itself runs. That still tells us the
            // transactions are internally consistent with the statement.
            if (!asIsMatches && !invertedMatches)
            {
                bool asIsMagnitude = asIs.Abs() == ledger.Abs();
                bool invertedMagnitude = inverted.Abs() == ledger.Abs();

                if (asIsMagnitude && !invertedMagnitude)
                {
                    return Build(
                        SignPolarity.AsIs,
                        SignConfidence.Likely,
                        $"The totals agree with the statement, though its balance is stated the other way round ({ledger.ToAccountingString()} against {asIs.ToAccountingString()}). The transactions themselves look right as written.");
                }

                if (invertedMagnitude && !asIsMagnitude)
                {
                    return Build(
                        SignPolarity.Inverted,
                        SignConfidence.Likely,
                        $"The amounts look reversed: flipping them gives {inverted.ToAccountingString()}, which matches the size of the statement's {ledger.ToAccountingString()}.");
                }
            }
        }

        return FromTransactionTypes(rows, Build, ledgerBalance, asIs);
    }

    private static SignVerdict FromTransactionTypes(
        IReadOnlyList<ImportedTransaction> rows,
        Func<SignPolarity, SignConfidence, string, SignVerdict> build,
        Money? ledger,
        Money asIs)
    {
        int agree = 0;
        int disagree = 0;

        foreach (ImportedTransaction row in rows)
        {
            string? type = row.TransactionType?.Trim();

            if (string.IsNullOrEmpty(type) || row.Amount.IsZero)
            {
                continue;
            }

            bool expectsNegative = MoneyOutTypes.Contains(type);
            bool expectsPositive = MoneyInTypes.Contains(type);

            // XFER and OTHER genuinely go either way, so they are not evidence.
            if (!expectsNegative && !expectsPositive)
            {
                continue;
            }

            bool matches = expectsNegative ? row.Amount.IsNegative : row.Amount.IsPositive;

            if (matches)
            {
                agree++;
            }
            else
            {
                disagree++;
            }
        }

        int classifiable = agree + disagree;

        if (classifiable < MinimumTypedRows)
        {
            string note = ledger is null
                ? "The statement gives no closing balance, so the amounts could not be cross-checked. Check the balance after importing."
                : $"The statement's balance does not match either reading. Importing as written gives {asIs.ToAccountingString()}; if that is wrong you may be missing earlier transactions.";

            return build(SignPolarity.AsIs, SignConfidence.Unknown, note);
        }

        double disagreementRatio = (double)disagree / classifiable;

        if (disagreementRatio >= 0.95 && classifiable >= 20)
        {
            return build(
                SignPolarity.Inverted,
                SignConfidence.Certain,
                $"All {classifiable} typed transactions run the wrong way — purchases are recorded as money in. The signs need reversing.");
        }

        if (disagreementRatio >= 0.8)
        {
            return build(
                SignPolarity.Inverted,
                SignConfidence.Likely,
                $"{disagree} of {classifiable} transactions are signed opposite to their stated type, which suggests this issuer reverses them.");
        }

        if (disagreementRatio <= 0.2)
        {
            return build(
                SignPolarity.AsIs,
                SignConfidence.Likely,
                "The transaction types agree with the amounts' signs, so the file reads correctly as written.");
        }

        return build(
            SignPolarity.AsIs,
            SignConfidence.Unknown,
            "The transaction types are mixed, so the direction could not be confirmed. Check the balance after importing.");
    }

    /// <summary>
    /// Refuses to suggest reversing a bank statement on anything short of proof.
    /// </summary>
    /// <remarks>
    /// Chequing and savings statements are essentially never reversed in the wild, so a false
    /// positive there would be all cost and no benefit — it would invert a correct statement
    /// on the strength of a heuristic.
    /// </remarks>
    private static SignVerdict Guard(SignVerdict verdict, ImportedAccountKind kind)
    {
        if (kind != ImportedAccountKind.Bank
            || verdict.Suggested != SignPolarity.Inverted
            || verdict.Confidence == SignConfidence.Certain)
        {
            return verdict;
        }

        return verdict with
        {
            Suggested = SignPolarity.AsIs,
            Confidence = SignConfidence.Unknown,
            Explanation =
                "Some amounts are signed oddly for their type, but bank statements are almost never reversed, so the file is being taken as written. Check the balance after importing.",
        };
    }
}
