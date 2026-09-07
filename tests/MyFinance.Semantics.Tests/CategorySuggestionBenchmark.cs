using System.Globalization;
using System.Text;
using MyFinance.Import.Categorization;
using MyFinance.Import.Mny;

namespace MyFinance.Semantics.Tests;

internal static class MoneyFile
{
    private static readonly Lazy<string?> Located = new(Locate);

    public static string? Path => Located.Value;

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            FileInfo[] found = directory.GetFiles("*.mny", SearchOption.TopDirectoryOnly);

            if (found.Length > 0)
            {
                return found[0].FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

public sealed class MoneyFileFactAttribute : FactAttribute
{
    public MoneyFileFactAttribute()
    {
        if (MoneyFile.Path is null)
        {
            Skip = "No .mny file was found; the benchmark needs real history to measure against.";
        }
    }
}

/// <summary>
/// Measures whether embedding similarity actually improves category suggestions.
/// </summary>
/// <remarks>
/// <para>
/// The question this answers is not "does the model produce plausible vectors" but "does
/// adding it get more transactions filed correctly than the chain already in place". Those
/// are different questions and only the second justifies shipping 40 MB.
/// </para>
/// <para>
/// The split is by date, oldest to newest, because a random split lets the model see a
/// merchant's future and score itself on its own past. Every real import is a guess about
/// transactions later than everything already in the book, and the measurement has to match.
/// </para>
/// </remarks>
public sealed class CategorySuggestionBenchmark
{
    private sealed record Row(DateOnly Date, string Payee, int CategoryId);

    [MoneyFileFact]
    public void Report()
    {
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);
        Dictionary<int, string> payees = book.Payees.ToDictionary(p => p.Id, p => p.Name);

        List<Row> rows =
        [
            .. book.TopLevelTransactions
                .Where(t => !t.IsTransfer && t.CategoryId is not null && t.PayeeId is not null)
                .Where(t => payees.ContainsKey(t.PayeeId!.Value))
                .Select(t => new Row(t.Date, payees[t.PayeeId!.Value], t.CategoryId!.Value))
                .OrderBy(t => t.Date),
        ];

        int split = (int)(rows.Count * 0.95);
        List<Row> train = [.. rows.Take(split)];
        List<Row> test = [.. rows.Skip(split)];

        var report = new StringBuilder();
        void Say(string line)
        {
            report.AppendLine(line);
            Console.WriteLine(line);
        }

        Say($"rows={rows.Count}  train={train.Count}  test={test.Count}");
        Say($"train spans {train[0].Date} to {train[^1].Date}; test {test[0].Date} to {test[^1].Date}");

        // -- the chain as it stands today ------------------------------------------------
        var memory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Row row in train)
        {
            memory[row.Payee] = row.CategoryId;
        }

        CategoryClassifier bayes = CategoryClassifier.Train(
            train.Select(r => new TrainingExample(r.Payee, r.CategoryId)));

        Say($"classifier trained on {bayes.TrainingSize} rows over {bayes.CategoryCount} categories");

        // -- the vector index over the merchants seen in training ------------------------
        using var embedder = OnnxTextEmbedder.Create();
        embedder.IsAvailable.ShouldBeTrue(embedder.Failure);

        Dictionary<string, int> trainPayees = new(StringComparer.OrdinalIgnoreCase);

        foreach (Row row in train)
        {
            trainPayees[row.Payee] = row.CategoryId;
        }

        string[] known = [.. trainPayees.Keys];
        string[] unseen = [.. test.Select(t => t.Payee).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !trainPayees.ContainsKey(p))];

        var clock = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<float[]> knownVectors = embedder.Embed(known);
        clock.Stop();
        Say($"embedded {known.Length} known merchants in {clock.Elapsed.TotalSeconds:F1}s "
            + $"({known.Length / Math.Max(clock.Elapsed.TotalSeconds, 0.001):F0}/sec)");

        IReadOnlyList<float[]> unseenVectors = embedder.Embed(unseen);
        var queryFor = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < unseen.Length; i++)
        {
            queryFor[unseen[i]] = unseenVectors[i];
        }

        SimilarityIndex index = SimilarityIndex.Build(
            known.Select((name, i) => new SimilarityEntry(i, name, trainPayees[name], knownVectors[i])));

        // -- how far does today's chain get? ---------------------------------------------
        int memoryHit = 0, memoryRight = 0, bayesHit = 0, bayesRight = 0;
        var uncovered = new List<Row>();

        foreach (Row row in test)
        {
            if (memory.TryGetValue(row.Payee, out int remembered))
            {
                memoryHit++;
                if (remembered == row.CategoryId) memoryRight++;
                continue;
            }

            if (bayes.Predict(row.Payee) is CategoryPrediction p)
            {
                bayesHit++;
                if (p.CategoryId == row.CategoryId) bayesRight++;
                continue;
            }

            uncovered.Add(row);
        }

        Say("");
        Say("today's chain, on the held-out period:");
        Say($"  payee memory   answered {Share(memoryHit, test.Count)}  correct {Share(memoryRight, memoryHit)}");
        Say($"  bayes          answered {Share(bayesHit, test.Count)}  correct {Share(bayesRight, bayesHit)}");
        Say($"  ANSWERED BY NEITHER: {uncovered.Count} rows ({100.0 * uncovered.Count / test.Count:F1}%)");

        // -- what similarity adds on exactly those rows ----------------------------------
        Say("");
        Say($"embedding similarity on those {uncovered.Count} otherwise-uncategorized rows:");
        Say("   floor  conf   answered            correct");

        foreach (double floor in new[] { 0.40, 0.50, 0.55, 0.60, 0.65, 0.70, 0.75, 0.80 })
        {
            foreach (double confidence in new[] { 0.0, 0.5 })
            {
                int answered = 0, right = 0;

                foreach (Row row in uncovered)
                {
                    if (!queryFor.TryGetValue(row.Payee, out float[]? q))
                    {
                        continue;
                    }

                    if (index.Predict(q, confidence, SimilarityIndex.DefaultNeighbours, floor)
                        is SimilarityPrediction prediction)
                    {
                        answered++;
                        if (prediction.CategoryId == row.CategoryId) right++;
                    }
                }

                Say($"   {floor:F2}   {confidence:F1}   {Share(answered, uncovered.Count),-18}  {Share(right, answered)}");
            }
        }

        // -- the cheaper alternative, on exactly the same rows ---------------------------
        // Character n-grams with TF-IDF weighting and the same cosine kNN. No model, no
        // native dependency, no download. If this matches the neural numbers then the 40 MB
        // is buying nothing.
        var ngram = new NgramIndex(known.Select((n, i) => (n, trainPayees[n])));

        Say("");
        Say($"character n-grams on the same {uncovered.Count} rows:");
        Say("   floor  conf   answered            correct");

        foreach (double floor in new[] { 0.30, 0.40, 0.50, 0.60, 0.70, 0.80 })
        {
            foreach (double confidence in new[] { 0.0, 0.5 })
            {
                int answered = 0, right = 0;

                foreach (Row row in uncovered)
                {
                    if (ngram.Predict(row.Payee, confidence, floor) is int guess)
                    {
                        answered++;
                        if (guess == row.CategoryId) right++;
                    }
                }

                Say($"   {floor:F2}   {confidence:F1}   {Share(answered, uncovered.Count),-18}  {Share(right, answered)}");
            }
        }

        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "myfinance-suggestion-benchmark.txt"),
            report.ToString());
    }

    /// <summary>A TF-IDF character n-gram index, for comparison only.</summary>
    private sealed class NgramIndex
    {
        private readonly List<(Dictionary<string, double> Vector, int CategoryId)> _entries = [];
        private readonly Dictionary<string, double> _idf = new(StringComparer.Ordinal);

        public NgramIndex(IEnumerable<(string Name, int CategoryId)> entries)
        {
            List<(string Name, int CategoryId)> all = [.. entries];
            var documentCount = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach ((string name, _) in all)
            {
                foreach (string gram in Grams(name).Keys)
                {
                    documentCount[gram] = documentCount.GetValueOrDefault(gram) + 1;
                }
            }

            foreach ((string gram, int count) in documentCount)
            {
                _idf[gram] = Math.Log((all.Count + 1.0) / (count + 1.0)) + 1.0;
            }

            foreach ((string name, int categoryId) in all)
            {
                _entries.Add((Weight(name), categoryId));
            }
        }

        private static Dictionary<string, int> Grams(string text)
        {
            string clean = " " + new string([.. text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ')]) + " ";
            var grams = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int n = 3; n <= 5; n++)
            {
                for (int i = 0; i + n <= clean.Length; i++)
                {
                    string g = clean.Substring(i, n);
                    if (!g.Trim().Equals(string.Empty, StringComparison.Ordinal))
                    {
                        grams[g] = grams.GetValueOrDefault(g) + 1;
                    }
                }
            }

            return grams;
        }

        private Dictionary<string, double> Weight(string text)
        {
            var vector = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach ((string gram, int count) in Grams(text))
            {
                vector[gram] = (1 + Math.Log(count)) * _idf.GetValueOrDefault(gram, 1.0);
            }

            double magnitude = Math.Sqrt(vector.Values.Sum(v => v * v));

            if (magnitude > 0)
            {
                foreach (string key in vector.Keys.ToList())
                {
                    vector[key] /= magnitude;
                }
            }

            return vector;
        }

        public int? Predict(string text, double confidenceThreshold, double floor)
        {
            Dictionary<string, double> query = Weight(text);
            var scored = new List<(double Similarity, int CategoryId)>();

            foreach ((Dictionary<string, double> vector, int categoryId) in _entries)
            {
                double dot = 0;

                // Iterate the shorter side; these vectors are sparse.
                foreach ((string gram, double weight) in query)
                {
                    if (vector.TryGetValue(gram, out double other))
                    {
                        dot += weight * other;
                    }
                }

                if (dot >= floor)
                {
                    scored.Add((dot, categoryId));
                }
            }

            if (scored.Count == 0)
            {
                return null;
            }

            var weights = new Dictionary<int, double>();

            foreach ((double similarity, int categoryId) in scored.OrderByDescending(s => s.Similarity).Take(5))
            {
                weights[categoryId] = weights.GetValueOrDefault(categoryId) + similarity;
            }

            double total = weights.Values.Sum();
            (int best, double bestWeight) = weights.OrderByDescending(p => p.Value).First();

            return bestWeight / total >= confidenceThreshold ? best : null;
        }
    }

    private static string Share(int part, int whole) => whole == 0
        ? "0 (n/a)"
        : string.Create(CultureInfo.InvariantCulture, $"{part,5} ({100.0 * part / whole,5:F1}%)");
}
