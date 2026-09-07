namespace MyFinance.Import.Categorization;

/// <summary>One thing the index can match against: a past merchant and how it was filed.</summary>
/// <param name="Key">Identifier of whatever this stands for, usually a payee.</param>
/// <param name="Label">Its name, so a suggestion can say what it matched.</param>
/// <param name="CategoryId">The category it was filed under.</param>
/// <param name="Vector">Unit-length embedding of its text.</param>
public sealed record SimilarityEntry(int Key, string Label, int CategoryId, float[] Vector);

/// <summary>A past merchant that resembles the one being categorized.</summary>
/// <param name="Key">The entry's key.</param>
/// <param name="Label">Its name, for the explanation shown to the user.</param>
/// <param name="CategoryId">The category being proposed.</param>
/// <param name="Similarity">Cosine similarity, between minus one and one.</param>
public sealed record SimilarNeighbour(int Key, string Label, int CategoryId, double Similarity);

/// <summary>
/// What the index concluded: a category, how sure it is, and what convinced it.
/// </summary>
/// <param name="CategoryId">The proposed category.</param>
/// <param name="Confidence">Share of the weight behind the winning category.</param>
/// <param name="Best">The single closest match, for explaining the suggestion.</param>
public sealed record SimilarityPrediction(int CategoryId, double Confidence, SimilarNeighbour Best);

/// <summary>
/// Finds the past merchants a new one most resembles.
/// </summary>
/// <remarks>
/// <para>
/// Pure arithmetic over vectors somebody else produced, so it can be tested exactly, with
/// hand-written vectors and no model anywhere near it. That separation is deliberate: the
/// embedder is the part that cannot be asserted, and keeping it out of here means the
/// ranking, the voting and the thresholds all can be.
/// </para>
/// <para>
/// Vectors are assumed unit length — <see cref="ITextEmbedder" /> guarantees it — so cosine
/// similarity is a dot product.
/// </para>
/// </remarks>
public sealed class SimilarityIndex
{
    /// <summary>
    /// How many neighbours vote.
    /// </summary>
    /// <remarks>
    /// More than one because the nearest single match can be a coincidence; few enough that
    /// a large category cannot outvote a genuinely close match on weight of numbers alone.
    /// </remarks>
    public const int DefaultNeighbours = 5;

    /// <summary>
    /// How close a match has to be before it is worth mentioning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured, not guessed. Against a real book of 17,559 categorized transactions — trained
    /// on everything to Nov 2019 and tested on the six years after — this source only ever
    /// sees the roughly quarter of rows that payee memory and the classifier both decline.
    /// On those, precision against the floor came out:
    /// 0.60 → 55%, 0.70 → 58%, <b>0.80 → 63%</b>, while coverage of the gap fell from 54% to 16%.
    /// </para>
    /// <para>
    /// 0.80 is chosen deliberately at the conservative end. This is the least reliable of the
    /// suggestion sources and the only one that fires where the user has no history at all, so
    /// it should speak rarely and be right when it does. Short merchant strings embed into a
    /// narrow band — two unrelated shops sit around 0.4 — which is why a floor that sounds
    /// permissive is in fact strict.
    /// </para>
    /// </remarks>
    public const double DefaultSimilarityFloor = 0.80;

    /// <summary>
    /// How much of the neighbourhood's weight must agree before a category is proposed.
    /// </summary>
    /// <remarks>
    /// Deliberately lower than the classifier's 0.65: a handful of neighbours cannot produce
    /// the sharp posteriors a token model does, and requiring one here would silence the
    /// source entirely. The floor above is what does the real filtering.
    /// </remarks>
    public const double DefaultConfidenceThreshold = 0.5;

    private readonly SimilarityEntry[] _entries;

    private SimilarityIndex(SimilarityEntry[] entries) => _entries = entries;

    public static SimilarityIndex Empty { get; } = new([]);

    public int Count => _entries.Length;

    public bool IsUseful => _entries.Length > 0;

    public static SimilarityIndex Build(IEnumerable<SimilarityEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return new SimilarityIndex([.. entries.Where(e => e.Vector.Length > 0)]);
    }

    /// <summary>The closest entries to a query vector, nearest first.</summary>
    public IReadOnlyList<SimilarNeighbour> Nearest(
        float[] query,
        int count = DefaultNeighbours,
        double similarityFloor = DefaultSimilarityFloor)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_entries.Length == 0 || query.Length == 0 || count <= 0)
        {
            return [];
        }

        var found = new List<SimilarNeighbour>();

        foreach (SimilarityEntry entry in _entries)
        {
            if (entry.Vector.Length != query.Length)
            {
                continue;
            }

            double similarity = Dot(query, entry.Vector);

            if (similarity >= similarityFloor)
            {
                found.Add(new SimilarNeighbour(entry.Key, entry.Label, entry.CategoryId, similarity));
            }
        }

        // Ties broken by label so the same book always explains itself the same way.
        return
        [
            .. found
                .OrderByDescending(n => n.Similarity)
                .ThenBy(n => n.Label, StringComparer.Ordinal)
                .Take(count),
        ];
    }

    /// <summary>
    /// The category the nearest neighbours agree on, or null when they do not agree enough.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neighbours vote, weighted by how far past the floor they sit rather than by their raw
    /// similarity. That rescaling matters: every neighbour is above the floor by definition,
    /// so raw cosines arrive in a narrow band — at a floor of 0.80 they all fall between 0.80
    /// and 1.0 — and voting on those is very nearly an unweighted count. A near-exact match at
    /// 0.98 would then lose to four vague ones at 0.81, which for a merchant this book has
    /// seen before is the wrong answer.
    /// </para>
    /// <para>
    /// Measuring the weight from the floor instead stretches that band back out, so the
    /// example above becomes 0.90 against four times 0.05 and closeness decides it.
    /// Confidence is the winner's share of the total weight, which keeps it comparable to the
    /// classifier's posterior when both are shown to the user as a percentage.
    /// </para>
    /// </remarks>
    public SimilarityPrediction? Predict(
        float[] query,
        double confidenceThreshold = DefaultConfidenceThreshold,
        int count = DefaultNeighbours,
        double similarityFloor = DefaultSimilarityFloor)
    {
        IReadOnlyList<SimilarNeighbour> neighbours = Nearest(query, count, similarityFloor);

        if (neighbours.Count == 0)
        {
            return null;
        }

        var weights = new Dictionary<int, double>();

        // A small floor of its own, so a single neighbour sitting exactly on the threshold
        // still carries weight rather than voting with zero.
        const double Epsilon = 0.02;
        double span = Math.Max(1.0 - similarityFloor, Epsilon) + Epsilon;

        foreach (SimilarNeighbour neighbour in neighbours)
        {
            double share = (neighbour.Similarity - similarityFloor + Epsilon) / span;

            weights[neighbour.CategoryId] =
                weights.GetValueOrDefault(neighbour.CategoryId) + Math.Max(share, 0);
        }

        double total = weights.Values.Sum();

        if (total <= 0)
        {
            return null;
        }

        (int categoryId, double weight) = weights
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .First();

        double confidence = weight / total;

        if (confidence < confidenceThreshold)
        {
            return null;
        }

        SimilarNeighbour best = neighbours.First(n => n.CategoryId == categoryId);
        return new SimilarityPrediction(categoryId, confidence, best);
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;

        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * (double)b[i];
        }

        return sum;
    }
}
