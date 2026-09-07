using MyFinance.Core.Primitives;

namespace MyFinance.Import.Categorization;

/// <summary>Where a suggested category came from, in descending order of authority.</summary>
public enum SuggestionSource
{
    /// <summary>Nothing suggested one.</summary>
    None = 0,

    /// <summary>A rule the user wrote. The only deliberate instruction of the four.</summary>
    Rule = 1,

    /// <summary>The file itself named a category, which QIF exports do.</summary>
    FileCategory = 2,

    /// <summary>The payee's remembered category from the last time it was used.</summary>
    PayeeMemory = 3,

    /// <summary>Inferred from how similar transactions were categorized before.</summary>
    Statistical = 4,

    /// <summary>
    /// Taken from the merchant this one most resembles.
    /// </summary>
    /// <remarks>
    /// Last because it is the least reliable — measured at around 63% correct against a real
    /// book — and the only source that fires where the user has no history to draw on at all.
    /// It earns its place by covering rows nothing else can, not by being trustworthy.
    /// </remarks>
    Similar = 5,

    /// <summary>
    /// From the merchant's industry code, where the bank supplied one.
    /// </summary>
    /// <remarks>
    /// A fact about the merchant rather than a guess, but it maps to a general category and
    /// not necessarily the one this person files that shop under — somebody may well keep
    /// warehouse clubs under Household rather than Groceries. Hence last.
    /// </remarks>
    Sic = 6,
}

/// <summary>A proposed category, with where it came from and how sure we are.</summary>
public sealed record CategorySuggestion
{
    public static CategorySuggestion None { get; } =
        new() { CategoryId = null, Source = SuggestionSource.None, Confidence = 0 };

    public int? CategoryId { get; init; }

    public required SuggestionSource Source { get; init; }

    /// <summary>Between zero and one. Only the statistical source produces a partial value.</summary>
    public required double Confidence { get; init; }

    /// <summary>A payee the rule wants the transaction rewritten to, when it named one.</summary>
    public int? PayeeId { get; init; }

    /// <summary>The rule that fired, so the preview can name it.</summary>
    public RuleSpec? Rule { get; init; }

    /// <summary>The past merchant a similarity match was based on, for the explanation.</summary>
    public string? SimilarTo { get; init; }

    public bool HasCategory => CategoryId is not null;

    /// <summary>
    /// Whether this came from something the user decided rather than something inferred.
    /// </summary>
    /// <remarks>
    /// The line between filling a box in and offering to. A rule was written deliberately, a
    /// file's category was decided in another program, and a payee's usual category is what
    /// this person did last time — all three can be applied without asking. The classifier and
    /// the lookalike are guesses, measured at roughly 78% and 63% correct, and a guess applied
    /// silently to a saved transaction is one nobody will ever notice was wrong.
    /// </remarks>
    public bool IsCertain => Source is SuggestionSource.Rule
        or SuggestionSource.FileCategory
        or SuggestionSource.PayeeMemory;

    /// <summary>Short label for the preview, e.g. "Rule: Shell → Fuel".</summary>
    public string Describe() => Source switch
    {
        SuggestionSource.Rule => Rule is null ? "Rule" : $"Rule: {Rule.Name}",
        SuggestionSource.FileCategory => "From the file",
        SuggestionSource.PayeeMemory => "Usual for this payee",
        SuggestionSource.Statistical => $"Suggested ({Confidence:P0})",

        // Named, not just scored. A guess the user cannot see the reasoning behind is one
        // they have to check from scratch, which costs more than making it themselves.
        SuggestionSource.Similar => SimilarTo is null
            ? $"Looks familiar ({Confidence:P0})"
            : $"Looks like {SimilarTo}",
        SuggestionSource.Sic => "From the merchant's industry code",
        _ => string.Empty,
    };
}

/// <summary>
/// Decides what category to propose for an incoming transaction.
/// </summary>
/// <remarks>
/// <para>
/// Four sources, consulted in a fixed order of authority: a rule the user wrote, then a
/// category the file itself carried, then what this payee was last filed under, then a guess
/// from the statistical model. The order is deliberate — each step down is one step further
/// from something the user actually decided.
/// </para>
/// <para>
/// A rule beats the file's own category because the rule is a standing instruction in this
/// application, whereas the file's category is a decision made in another program at some
/// point in the past. When someone writes "Shell goes to Fuel" they mean it to win.
/// </para>
/// </remarks>
public static class CategorySuggester
{
    /// <summary>Everything known about the transaction being categorized.</summary>
    /// <param name="AccountId">The account it is going into.</param>
    /// <param name="PayeeName">The payee as it will be recorded.</param>
    /// <param name="Descriptor">The raw text the bank wrote.</param>
    /// <param name="Amount">Signed amount.</param>
    /// <param name="FileCategoryId">
    /// The category the file named, once resolved against the book. Null when the file named
    /// none, or named one this book does not have.
    /// </param>
    /// <param name="PayeeLastCategoryId">What this payee was last filed under.</param>
    /// <param name="Vector">
    /// The transaction text embedded, when an embedder was available. Null leaves the
    /// similarity source silent, which is how the whole feature stays optional.
    /// </param>
    public readonly record struct SuggestionInput(
        int AccountId,
        string? PayeeName,
        string? Descriptor,
        Money Amount,
        int? FileCategoryId,
        int? PayeeLastCategoryId,
        float[]? Vector = null,
        string? MerchantCode = null);

    public static CategorySuggestion Suggest(
        SuggestionInput input,
        IEnumerable<RuleSpec> rules,
        CategoryClassifier classifier,
        double confidenceThreshold = CategoryClassifier.DefaultConfidenceThreshold,
        SimilarityIndex? similar = null,
        IReadOnlyDictionary<string, int>? merchantCodes = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(classifier);

        RuleMatch? rule = RuleEvaluator.FirstMatch(
            rules,
            new RuleInput(input.AccountId, input.PayeeName, input.Descriptor, input.Amount));

        // A rule can rewrite the payee without naming a category, so a match still counts
        // even when it has nothing to file the transaction under.
        if (rule is not null && (rule.CategoryId is not null || rule.PayeeId is not null))
        {
            return new CategorySuggestion
            {
                CategoryId = rule.CategoryId,
                PayeeId = rule.PayeeId,
                Source = SuggestionSource.Rule,
                Confidence = 1,
                Rule = rule.Rule,
            };
        }

        if (input.FileCategoryId is int fromFile)
        {
            return new CategorySuggestion
            {
                CategoryId = fromFile,
                Source = SuggestionSource.FileCategory,
                Confidence = 1,
            };
        }

        if (input.PayeeLastCategoryId is int remembered)
        {
            return new CategorySuggestion
            {
                CategoryId = remembered,
                Source = SuggestionSource.PayeeMemory,
                Confidence = 1,
            };
        }

        string text = string.Join(
            " ",
            new[] { input.PayeeName, input.Descriptor }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        if (classifier.Predict(text, confidenceThreshold) is CategoryPrediction prediction)
        {
            return new CategorySuggestion
            {
                CategoryId = prediction.CategoryId,
                Source = SuggestionSource.Statistical,
                Confidence = prediction.Confidence,
            };
        }

        // Last resort: the merchant this one most resembles. Only reached when the user has
        // no rule, the file said nothing, this payee is new, and there are not even shared
        // words to go on.
        if (input.Vector is float[] vector
            && similar is { IsUseful: true }
            && similar.Predict(vector) is SimilarityPrediction match)
        {
            return new CategorySuggestion
            {
                CategoryId = match.CategoryId,
                Source = SuggestionSource.Similar,
                Confidence = match.Confidence,
                SimilarTo = match.Best.Label,
            };
        }

        if (input.MerchantCode is string code
            && merchantCodes is not null
            && merchantCodes.TryGetValue(code, out int fromCode))
        {
            return new CategorySuggestion
            {
                CategoryId = fromCode,
                Source = SuggestionSource.Sic,
                Confidence = 1,
            };
        }

        return CategorySuggestion.None;
    }
}
