using MyFinance.Core.Payees;
using MyFinance.Core.Primitives;
using MyFinance.Import.Model;

namespace MyFinance.Import.Dedupe;

/// <summary>A transaction already in the register, reduced to what duplicate matching needs.</summary>
/// <param name="Id">Database id.</param>
/// <param name="Date">Posted date.</param>
/// <param name="Amount">Signed amount.</param>
/// <param name="ExternalId">The bank's own id, when this row came from an OFX import.</param>
/// <param name="PayeeName">Payee name, for the fuzzy comparison.</param>
public sealed record ExistingTransaction(
    int Id,
    DateOnly Date,
    Money Amount,
    string? ExternalId,
    string? PayeeName);

/// <summary>How confident we are that an incoming row is already recorded.</summary>
public enum DuplicateKind
{
    /// <summary>Nothing like it in the register.</summary>
    None = 0,

    /// <summary>
    /// The bank's own id is already present. Certain, and not overridable — importing it
    /// would violate the unique index and abort the whole batch. Only OFX files carry one;
    /// QIF has no such thing, which is why it can only ever be matched approximately.
    /// </summary>
    ExternalId = 1,

    /// <summary>Appeared twice in this one file.</summary>
    RepeatedInFile = 2,

    /// <summary>Same amount, near date, same payee. Excluded by default but reversible.</summary>
    Likely = 3,

    /// <summary>Same amount and near date but the payee differs, or several rows match.</summary>
    Possible = 4,
}

/// <summary>The verdict on one incoming row.</summary>
/// <param name="Kind">How sure we are.</param>
/// <param name="ExistingId">The register row it appears to duplicate, when there is one.</param>
/// <param name="Explanation">What to show beside the row.</param>
public sealed record DuplicateVerdict(DuplicateKind Kind, int? ExistingId, string? Explanation)
{
    public static DuplicateVerdict None { get; } = new(DuplicateKind.None, null, null);

    /// <summary>True when the row cannot be imported at all.</summary>
    public bool IsBlocked => Kind is DuplicateKind.ExternalId or DuplicateKind.RepeatedInFile;

    /// <summary>Whether the row starts out unticked in the preview.</summary>
    public bool ExcludedByDefault => Kind is DuplicateKind.ExternalId or DuplicateKind.RepeatedInFile or DuplicateKind.Likely;
}

/// <summary>
/// Works out which rows of a download are already in the register.
/// </summary>
/// <remarks>
/// Overlapping statements are the normal case, not the exception: nearly every download
/// repeats the tail of the one before it. Getting this wrong in one direction duplicates
/// every transaction; in the other it silently drops real ones. So certainty is graded, and
/// only an outright FITID collision is treated as beyond argument.
/// </remarks>
public static class DuplicateDetector
{
    /// <summary>How far a posted date may drift and still be considered the same item.</summary>
    public const int FuzzyDateToleranceDays = 3;

    /// <summary>
    /// Classifies every row of a statement against the register.
    /// </summary>
    /// <param name="rows">The incoming transactions, in file order.</param>
    /// <param name="existing">Transactions already recorded in the target account.</param>
    /// <returns>One verdict per incoming row, in the same order.</returns>
    public static IReadOnlyList<DuplicateVerdict> Detect(
        IReadOnlyList<ImportedTransaction> rows,
        IReadOnlyList<ExistingTransaction> existing)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(existing);

        var knownExternalIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (ExistingTransaction row in existing)
        {
            if (!string.IsNullOrWhiteSpace(row.ExternalId))
            {
                knownExternalIds.TryAdd(row.ExternalId, row.Id);
            }
        }

        // A register row may only account for one incoming row. Without this, three identical
        // coffees in one week would all match the same existing transaction and two real ones
        // would be silently discarded.
        var claimed = new HashSet<int>();
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var verdicts = new List<DuplicateVerdict>(rows.Count);

        foreach (ImportedTransaction row in rows)
        {
            verdicts.Add(Classify(row, existing, knownExternalIds, seenInFile, claimed));
        }

        return verdicts;
    }

    private static DuplicateVerdict Classify(
        ImportedTransaction row,
        IReadOnlyList<ExistingTransaction> existing,
        Dictionary<string, int> knownExternalIds,
        HashSet<string> seenInFile,
        HashSet<int> claimed)
    {
        if (!string.IsNullOrWhiteSpace(row.ExternalId))
        {
            if (knownExternalIds.TryGetValue(row.ExternalId, out int existingId))
            {
                return new DuplicateVerdict(
                    DuplicateKind.ExternalId,
                    existingId,
                    "Already imported — the bank's own reference for this transaction is in the register.");
            }

            // Files really do repeat a reference. Left unchecked this breaks the unique index
            // mid-commit and takes the entire batch down with it.
            if (!seenInFile.Add(row.ExternalId))
            {
                return new DuplicateVerdict(
                    DuplicateKind.RepeatedInFile,
                    null,
                    "This reference appears more than once in the file; only the first is imported.");
            }

            // The bank gave it an id we have never seen, which is as good as it gets.
            return DuplicateVerdict.None;
        }

        string incomingPayee = PayeeNormalizer.Normalize(row.Name ?? row.Memo);

        List<ExistingTransaction> candidates =
        [
            .. existing.Where(e =>
                !claimed.Contains(e.Id)
                && e.Amount == row.Amount
                && Math.Abs(e.Date.DayNumber - row.Posted.DayNumber) <= FuzzyDateToleranceDays)
        ];

        if (candidates.Count == 0)
        {
            return DuplicateVerdict.None;
        }

        ExistingTransaction? agreeing = candidates.FirstOrDefault(c =>
            incomingPayee.Length == 0
            || string.IsNullOrWhiteSpace(c.PayeeName)
            || string.Equals(PayeeNormalizer.Normalize(c.PayeeName), incomingPayee, StringComparison.Ordinal));

        if (candidates.Count == 1 && agreeing is not null)
        {
            claimed.Add(agreeing.Id);

            return new DuplicateVerdict(
                DuplicateKind.Likely,
                agreeing.Id,
                $"Matches a transaction already recorded on {agreeing.Date:d} for the same amount and payee.");
        }

        // Several candidates, or the payee disagrees. Included by default: three identical
        // amounts in a week is ordinary, and dropping real transactions is a loss the user
        // would never notice, whereas a duplicate they can see and delete.
        return new DuplicateVerdict(
            DuplicateKind.Possible,
            candidates[0].Id,
            $"There is a similar amount already recorded around {candidates[0].Date:d}. Check before importing.");
    }
}
