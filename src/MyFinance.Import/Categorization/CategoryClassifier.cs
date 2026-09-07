using System.Globalization;
using System.Text;

namespace MyFinance.Import.Categorization;

/// <summary>One already-categorized transaction, used as training data.</summary>
/// <param name="Text">Payee and memo joined — what the classifier learns from.</param>
/// <param name="CategoryId">The category the user actually chose.</param>
public sealed record TrainingExample(string Text, int CategoryId);

/// <summary>What the classifier thinks a transaction should be filed under.</summary>
/// <param name="CategoryId">The most likely category.</param>
/// <param name="Confidence">Posterior probability, between zero and one.</param>
/// <param name="SupportingExamples">How many past transactions back that category.</param>
public sealed record CategoryPrediction(int CategoryId, double Confidence, int SupportingExamples);

/// <summary>
/// Splits transaction text into the words the classifier reasons about.
/// </summary>
/// <remarks>
/// Deliberately blunt. The signal in a bank descriptor is almost entirely in the merchant's
/// name; store numbers, reference codes and dates vary between visits and would only ever be
/// learned as noise, so they are dropped rather than diluting the words that matter.
/// </remarks>
public static class TextTokenizer
{
    /// <summary>Words too common to carry any signal about a category.</summary>
    private static readonly HashSet<string> StopWords =
        new(StringComparer.Ordinal)
        {
            "the", "and", "for", "from", "with", "inc", "llc", "ltd", "co", "of", "to", "at",
            "on", "in", "a", "an", "purchase", "payment", "debit", "credit", "card", "pos",
        };

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (char character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(char.ToLower(character, CultureInfo.CurrentCulture));
                continue;
            }

            AddToken(tokens, current);
        }

        AddToken(tokens, current);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        string token = builder.ToString();
        builder.Clear();

        // A single letter carries nothing, and a run of digits is a store or reference
        // number that differs on every visit to the same merchant.
        if (token.Length < 2 || token.All(char.IsAsciiDigit) || StopWords.Contains(token))
        {
            return;
        }

        tokens.Add(token);
    }
}

/// <summary>
/// Guesses a category from how the user has categorized similar transactions before.
/// </summary>
/// <remarks>
/// <para>
/// A multinomial naive Bayes classifier over the words in the payee and memo, trained on the
/// user's own history. It knows nothing about money in general — only about how this
/// particular person files things — which is exactly what makes it useful and also why it is
/// worthless on a new book.
/// </para>
/// <para>
/// Its output is never applied on its own. It fills in a suggestion the user can see and
/// override, below rules and payee memory, and it declines to answer at all when the
/// evidence is thin. A wrong category the user notices is a nuisance; a wrong category
/// applied silently to two hundred rows is a corrupted year of reports.
/// </para>
/// </remarks>
public sealed class CategoryClassifier
{
    /// <summary>Below this posterior probability the classifier says nothing.</summary>
    public const double DefaultConfidenceThreshold = 0.65;

    /// <summary>A category needs at least this many examples before it can be suggested.</summary>
    public const int MinimumExamplesPerCategory = 3;

    /// <summary>Below this many examples overall the classifier is not worth consulting.</summary>
    public const int MinimumTrainingSize = 20;

    private readonly Dictionary<int, int> _documentsPerCategory;
    private readonly Dictionary<int, Dictionary<string, int>> _tokenCounts;
    private readonly Dictionary<int, int> _totalTokensPerCategory;
    private readonly HashSet<string> _vocabulary;
    private readonly int _totalDocuments;

    private CategoryClassifier(
        Dictionary<int, int> documentsPerCategory,
        Dictionary<int, Dictionary<string, int>> tokenCounts,
        Dictionary<int, int> totalTokensPerCategory,
        HashSet<string> vocabulary,
        int totalDocuments)
    {
        _documentsPerCategory = documentsPerCategory;
        _tokenCounts = tokenCounts;
        _totalTokensPerCategory = totalTokensPerCategory;
        _vocabulary = vocabulary;
        _totalDocuments = totalDocuments;
    }

    /// <summary>A classifier that knows nothing and therefore never suggests anything.</summary>
    public static CategoryClassifier Empty { get; } = new([], [], [], [], 0);

    /// <summary>How many past transactions this was trained on.</summary>
    public int TrainingSize => _totalDocuments;

    public int CategoryCount => _documentsPerCategory.Count;

    /// <summary>
    /// True when there is enough history for the classifier's opinion to be worth having.
    /// </summary>
    public bool IsUseful => _totalDocuments >= MinimumTrainingSize && _documentsPerCategory.Count >= 2;

    public static CategoryClassifier Train(IEnumerable<TrainingExample> examples)
    {
        ArgumentNullException.ThrowIfNull(examples);

        var documentsPerCategory = new Dictionary<int, int>();
        var tokenCounts = new Dictionary<int, Dictionary<string, int>>();
        var totalTokensPerCategory = new Dictionary<int, int>();
        var vocabulary = new HashSet<string>(StringComparer.Ordinal);

        int documents = 0;

        foreach (TrainingExample example in examples)
        {
            IReadOnlyList<string> tokens = TextTokenizer.Tokenize(example.Text);

            // A transaction whose descriptor reduces to nothing teaches nothing, and counting
            // it would only inflate the prior of whatever category it happened to be in.
            if (tokens.Count == 0)
            {
                continue;
            }

            documents++;
            documentsPerCategory[example.CategoryId] =
                documentsPerCategory.GetValueOrDefault(example.CategoryId) + 1;

            if (!tokenCounts.TryGetValue(example.CategoryId, out Dictionary<string, int>? counts))
            {
                counts = new Dictionary<string, int>(StringComparer.Ordinal);
                tokenCounts[example.CategoryId] = counts;
            }

            foreach (string token in tokens)
            {
                counts[token] = counts.GetValueOrDefault(token) + 1;
                vocabulary.Add(token);
            }

            totalTokensPerCategory[example.CategoryId] =
                totalTokensPerCategory.GetValueOrDefault(example.CategoryId) + tokens.Count;
        }

        return new CategoryClassifier(
            documentsPerCategory,
            tokenCounts,
            totalTokensPerCategory,
            vocabulary,
            documents);
    }

    /// <summary>
    /// The most likely category for some transaction text, or null when nothing is likely
    /// enough to be worth showing.
    /// </summary>
    public CategoryPrediction? Predict(string? text, double confidenceThreshold = DefaultConfidenceThreshold)
    {
        if (!IsUseful)
        {
            return null;
        }

        IReadOnlyList<string> tokens = TextTokenizer.Tokenize(text);
        if (tokens.Count == 0)
        {
            return null;
        }

        // Without a single word in common with anything ever categorized, the "prediction"
        // would be the prior alone — that is, "whatever category you use most" — which is a
        // guess dressed up as a suggestion rather than evidence about this transaction.
        if (!tokens.Any(_vocabulary.Contains))
        {
            return null;
        }

        // Scored in logarithms: multiplying a dozen small probabilities in floating point
        // underflows to zero, and every category would then look equally likely.
        var scores = new Dictionary<int, double>(_documentsPerCategory.Count);

        foreach ((int categoryId, int documents) in _documentsPerCategory)
        {
            if (documents < MinimumExamplesPerCategory)
            {
                continue;
            }

            double score = Math.Log((double)documents / _totalDocuments);

            Dictionary<string, int> counts = _tokenCounts[categoryId];
            int totalTokens = _totalTokensPerCategory[categoryId];

            foreach (string token in tokens)
            {
                // Laplace smoothing: an unseen word must not drive the whole product to
                // zero, which is what would happen with a plain frequency.
                int occurrences = counts.GetValueOrDefault(token);
                score += Math.Log((occurrences + 1.0) / (totalTokens + _vocabulary.Count + 1.0));
            }

            scores[categoryId] = score;
        }

        // With a single candidate the posterior is one by construction, whatever the text
        // says, so the classifier would confidently file everything under the only category
        // it has ever seen. Two is the minimum at which the number means anything.
        if (scores.Count < 2)
        {
            return null;
        }

        (int bestCategory, double bestScore) = scores.MaxBy(pair => pair.Value);

        double confidence = Normalize(scores, bestScore);

        return confidence >= confidenceThreshold
            ? new CategoryPrediction(bestCategory, confidence, _documentsPerCategory[bestCategory])
            : null;
    }

    /// <summary>
    /// Turns log scores into a probability for the winner.
    /// </summary>
    /// <remarks>
    /// Subtracting the largest score before exponentiating is what keeps this from
    /// overflowing: the raw log-likelihoods are large negative numbers whose exponentials
    /// would otherwise all round to zero.
    /// </remarks>
    private static double Normalize(Dictionary<int, double> scores, double best)
    {
        double total = 0;

        foreach (double score in scores.Values)
        {
            total += Math.Exp(score - best);
        }

        return total <= 0 ? 0 : 1.0 / total;
    }
}
