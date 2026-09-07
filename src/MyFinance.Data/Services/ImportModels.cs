using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Import.Categorization;
using MyFinance.Import.Dedupe;
using MyFinance.Import.Model;
using MyFinance.Import.Payees;

namespace MyFinance.Data.Services;

/// <summary>One incoming transaction, with everything the preview needs to show about it.</summary>
public sealed record ImportCandidate
{
    /// <summary>Position in the file, and the key a decision refers back to.</summary>
    public required int Index { get; init; }

    public required ImportedTransaction Source { get; init; }

    /// <summary>The tidied payee name and the key an alias would be recorded under.</summary>
    public required CleanedDescriptor Descriptor { get; init; }

    /// <summary>Which payee this resolved to, and how.</summary>
    public required PayeeMatch Payee { get; init; }

    public required DuplicateVerdict Duplicate { get; init; }

    /// <summary>Where the proposed category came from, and how sure of it we are.</summary>
    public required CategorySuggestion Suggestion { get; init; }

    public int? SuggestedCategoryId => Suggestion.CategoryId;

    public string? SuggestedCategoryName { get; init; }

    /// <summary>Short label for the preview, e.g. "Rule: Shell" or "Suggested (82%)".</summary>
    public string SuggestionSourceText => Suggestion.Describe();

    /// <summary>
    /// True when the category was inferred statistically rather than decided by the user.
    /// The preview marks these so a guess is never mistaken for a certainty.
    /// </summary>
    public bool IsGuess => Suggestion.Source == SuggestionSource.Statistical;

    /// <summary>The category path the file named but this book does not have.</summary>
    public string? UnmatchedCategoryPath { get; init; }

    /// <summary>
    /// The file's own split lines, with their categories resolved. Empty unless the file
    /// carried more than one, which only QIF does.
    /// </summary>
    public IReadOnlyList<ResolvedSplit> FileSplits { get; init; } = [];

    public bool HasFileSplits => FileSplits.Count > 1;

    /// <summary>
    /// Every category path this row refers to that the book has no match for — the one on
    /// the transaction itself and any on its split lines.
    /// </summary>
    /// <remarks>
    /// The split lines have to be included or a split transaction's categories would never
    /// be offered for creation, because QIF puts them on the split lines rather than on the
    /// transaction, leaving the transaction-level path empty.
    /// </remarks>
    public IEnumerable<string> UnmatchedCategoryPaths
    {
        get
        {
            if (UnmatchedCategoryPath is string path)
            {
                yield return path;
            }

            foreach (ResolvedSplit split in FileSplits)
            {
                if (split.CategoryId is null && split.CategoryPath is string splitPath)
                {
                    yield return splitPath;
                }
            }
        }
    }

    public DateOnly Date => Source.Posted;

    /// <summary>Amount exactly as the file states it, before any sign correction.</summary>
    public Money StatedAmount => Source.Amount;

    /// <summary>Everything the bank wrote, kept for the memo so nothing is lost.</summary>
    public string RawDescriptor => Source.RawDescriptor;

    /// <summary>Whether this row starts out ticked in the preview.</summary>
    public bool IncludedByDefault => !Duplicate.ExcludedByDefault;

    public bool CanBeIncluded => !Duplicate.IsBlocked;
}

/// <summary>One split line from the file, matched against the book's categories.</summary>
/// <param name="CategoryId">The matching category, or null when the book has no such one.</param>
/// <param name="CategoryPath">The path exactly as the file wrote it.</param>
/// <param name="Memo">The note on this line.</param>
/// <param name="Amount">Signed, in the file's own convention.</param>
public sealed record ResolvedSplit(int? CategoryId, string? CategoryPath, string? Memo, Money Amount);

/// <summary>Everything worked out about a file before anything is written.</summary>
public sealed record ImportPreview
{
    public required Account Account { get; init; }

    public required ImportedStatement Statement { get; init; }

    public required IReadOnlyList<ImportCandidate> Candidates { get; init; }

    /// <summary>Whether the file's amounts need reversing, and how sure we are.</summary>
    public required SignVerdict Sign { get; init; }

    /// <summary>The account's balance before this import.</summary>
    public required Money CurrentBalance { get; init; }

    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }

    public int TotalRows => Candidates.Count;

    public int NewRows => Candidates.Count(c => c.Duplicate.Kind == DuplicateKind.None);

    public int DuplicateRows => Candidates.Count(c => c.Duplicate.Kind != DuplicateKind.None);

    /// <summary>Distinct payees that would be created, not rows needing one.</summary>
    public int NewPayees => Candidates
        .Where(c => c.Payee.IsNew && c.IncludedByDefault)
        .Select(c => c.Payee.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public int UncategorizedRows => Candidates.Count(c => c.SuggestedCategoryId is null && c.IncludedByDefault);

    /// <summary>How many rows a rule decided.</summary>
    public int RuleMatches => Candidates.Count(c => c.Suggestion.Source == SuggestionSource.Rule);

    /// <summary>How many rows the statistical model guessed at.</summary>
    public int Guesses => Candidates.Count(c => c.IsGuess && c.IncludedByDefault);

    /// <summary>
    /// Category paths the file referred to that this book has no match for. Offering to
    /// create these is how a ledger arrives from another program with its filing intact.
    /// </summary>
    public IReadOnlyList<string> MissingCategoryPaths =>
    [
        .. Candidates
            .SelectMany(c => c.UnmatchedCategoryPaths)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
    ];
}

/// <summary>What the user decided about one row.</summary>
public sealed class ImportRowDecision
{
    /// <summary>Matches <see cref="ImportCandidate.Index"/>.</summary>
    public required int Index { get; set; }

    public bool Include { get; set; } = true;

    /// <summary>The payee name to use, which the user may have corrected.</summary>
    public string? PayeeName { get; set; }

    public int? CategoryId { get; set; }
}

/// <summary>The instruction to write an import into the book.</summary>
public sealed class ImportRequest
{
    public required int AccountId { get; set; }

    /// <summary>File name only, for the import history. Never the full path.</summary>
    public string? SourceFileName { get; set; }

    /// <summary>Set when the statement's amounts run the wrong way for this account.</summary>
    public bool ReverseSigns { get; set; }

    /// <summary>
    /// Creates any category the file names that the book does not have, rather than leaving
    /// those rows uncategorized. Only ever true when the user asked for it.
    /// </summary>
    public bool CreateMissingCategories { get; set; }

    public required IReadOnlyList<ImportRowDecision> Rows { get; set; }
}

/// <summary>What an import actually did.</summary>
public sealed record ImportSummary
{
    public required int BatchId { get; init; }

    public required int Added { get; init; }

    public required int Skipped { get; init; }

    public required int PayeesCreated { get; init; }

    /// <summary>Descriptor-to-payee mappings remembered for next time.</summary>
    public required int AliasesRecorded { get; init; }

    /// <summary>Categories created because the file named them and the book lacked them.</summary>
    public int CategoriesCreated { get; init; }

    /// <summary>Rows a rule decided the category for.</summary>
    public int RuleMatches { get; init; }

    /// <summary>The account's balance once the import was applied.</summary>
    public required Money BalanceAfter { get; init; }

    /// <summary>The statement's stated closing balance, when it gave one.</summary>
    public Money? LedgerBalance { get; init; }

    /// <summary>True when the resulting balance agrees with the bank's own figure.</summary>
    public bool AgreesWithStatement => LedgerBalance is Money ledger && ledger == BalanceAfter;
}

/// <summary>One past import, for the history list.</summary>
public sealed record ImportHistoryEntry
{
    public required ImportBatch Batch { get; init; }

    /// <summary>Name of the account it went into, or null if that account has been deleted.</summary>
    public string? AccountName { get; init; }

    /// <summary>Rows still present in the register. Zero once the batch has been reverted.</summary>
    public required int RemainingTransactions { get; init; }

    public int Id => Batch.Id;

    public bool IsReverted => Batch.IsReverted;

    public bool CanRevert => !Batch.IsReverted && RemainingTransactions > 0;
}
