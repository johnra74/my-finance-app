using MyFinance.Core.Primitives;
using MyFinance.Import.Categorization;

namespace MyFinance.Import.Tests.Categorization;

/// <summary>
/// The ranking and voting, tested with vectors written by hand.
/// </summary>
/// <remarks>
/// No model anywhere near these. The embedder is the part that can only be spot-checked;
/// keeping the arithmetic separate means the part that decides what the user is shown can be
/// asserted exactly.
/// </remarks>
public sealed class SimilarityIndexTests
{
    /// <summary>A unit vector pointing at one axis, so similarities are easy to reason about.</summary>
    private static float[] Axis(int index, int size = 4)
    {
        var v = new float[size];
        v[index] = 1;
        return v;
    }

    /// <summary>A unit vector between two axes, at a chosen cosine to the first.</summary>
    private static float[] Between(int a, int b, double towardsB, int size = 4)
    {
        var v = new float[size];
        v[a] = (float)Math.Sqrt(1 - towardsB * towardsB);
        v[b] = (float)towardsB;
        return v;
    }

    private static SimilarityIndex Index(params SimilarityEntry[] entries) =>
        SimilarityIndex.Build(entries);

    [Fact]
    public void An_empty_index_says_nothing()
    {
        SimilarityIndex.Empty.IsUseful.ShouldBeFalse();
        SimilarityIndex.Empty.Nearest(Axis(0)).ShouldBeEmpty();
        SimilarityIndex.Empty.Predict(Axis(0)).ShouldBeNull();
    }

    [Fact]
    public void The_closest_entry_comes_first()
    {
        SimilarityIndex index = Index(
            new SimilarityEntry(1, "far", 10, Between(0, 1, 0.6)),
            new SimilarityEntry(2, "near", 20, Between(0, 1, 0.1)),
            new SimilarityEntry(3, "middle", 30, Between(0, 1, 0.4)));

        IReadOnlyList<SimilarNeighbour> found = index.Nearest(Axis(0), count: 3, similarityFloor: 0);

        found.Select(n => n.Label).ShouldBe(["near", "middle", "far"]);
        found[0].Similarity.ShouldBeGreaterThan(found[1].Similarity);
    }

    [Fact]
    public void Nothing_below_the_floor_is_returned()
    {
        SimilarityIndex index = Index(
            new SimilarityEntry(1, "close", 10, Between(0, 1, 0.1)),
            new SimilarityEntry(2, "distant", 20, Axis(1)));

        IReadOnlyList<SimilarNeighbour> found = index.Nearest(Axis(0), similarityFloor: 0.5);

        found.Count.ShouldBe(1);
        found[0].Label.ShouldBe("close");
    }

    [Fact]
    public void Only_the_requested_number_of_neighbours_come_back()
    {
        SimilarityIndex index = Index(
            [.. Enumerable.Range(0, 10).Select(i => new SimilarityEntry(i, $"m{i}", i, Axis(0)))]);

        index.Nearest(Axis(0), count: 3, similarityFloor: 0).Count.ShouldBe(3);
    }

    /// <summary>
    /// The reason the vote is weighted from the floor rather than by raw similarity: at a
    /// realistic floor every neighbour is already close, so an unscaled vote would let a
    /// crowd of vague matches outrank the merchant this actually is.
    /// </summary>
    [Fact]
    public void A_near_exact_match_beats_a_crowd_of_vague_ones()
    {
        // One at 0.98 against four at about 0.81, all above a floor of 0.80.
        SimilarityIndex index = Index(
            new SimilarityEntry(1, "FABRIKAM FUEL 5772", 10, Between(0, 1, Math.Sqrt(1 - (0.98 * 0.98)))),
            new SimilarityEntry(2, "vague a", 20, Between(0, 1, Math.Sqrt(1 - (0.81 * 0.81)))),
            new SimilarityEntry(3, "vague b", 20, Between(0, 1, Math.Sqrt(1 - (0.81 * 0.81)))),
            new SimilarityEntry(4, "vague c", 20, Between(0, 1, Math.Sqrt(1 - (0.81 * 0.81)))),
            new SimilarityEntry(5, "vague d", 20, Between(0, 1, Math.Sqrt(1 - (0.81 * 0.81)))));

        SimilarityPrediction? prediction = index.Predict(Axis(0), confidenceThreshold: 0);

        prediction.ShouldNotBeNull();
        prediction.CategoryId.ShouldBe(10);
        prediction.Best.Label.ShouldBe("FABRIKAM FUEL 5772");
    }

    [Fact]
    public void Neighbours_agreeing_on_a_category_raise_the_confidence()
    {
        SimilarityIndex agreed = Index(
            new SimilarityEntry(1, "a", 10, Axis(0)),
            new SimilarityEntry(2, "b", 10, Axis(0)));

        SimilarityIndex split = Index(
            new SimilarityEntry(1, "a", 10, Axis(0)),
            new SimilarityEntry(2, "b", 99, Axis(0)));

        agreed.Predict(Axis(0), 0, similarityFloor: 0)!.Confidence.ShouldBe(1.0, 0.001);
        split.Predict(Axis(0), 0, similarityFloor: 0)!.Confidence.ShouldBe(0.5, 0.001);
    }

    [Fact]
    public void A_neighbourhood_that_cannot_agree_is_not_reported()
    {
        SimilarityIndex index = Index(
            new SimilarityEntry(1, "a", 10, Axis(0)),
            new SimilarityEntry(2, "b", 20, Axis(0)));

        index.Predict(Axis(0), confidenceThreshold: 0.9, similarityFloor: 0).ShouldBeNull();
    }

    /// <summary>The suggestion has to be able to say what convinced it.</summary>
    [Fact]
    public void The_prediction_names_the_merchant_it_matched()
    {
        SimilarityIndex index = Index(
            new SimilarityEntry(7, "FABRIKAM FOODS MKT", 42, Axis(0)));

        SimilarityPrediction prediction = index.Predict(Axis(0), 0, similarityFloor: 0)!;

        prediction.Best.Label.ShouldBe("FABRIKAM FOODS MKT");
        prediction.Best.Key.ShouldBe(7);
    }

    [Fact]
    public void Vectors_of_a_different_width_are_ignored_rather_than_crashing()
    {
        SimilarityIndex index = Index(
            new SimilarityEntry(1, "wrong size", 10, Axis(0, size: 8)),
            new SimilarityEntry(2, "right size", 20, Axis(0)));

        IReadOnlyList<SimilarNeighbour> found = index.Nearest(Axis(0), similarityFloor: 0);

        found.Count.ShouldBe(1);
        found[0].Label.ShouldBe("right size");
    }

    [Fact]
    public void Ties_resolve_the_same_way_every_time()
    {
        SimilarityIndex index = Index(
            new SimilarityEntry(1, "zebra", 10, Axis(0)),
            new SimilarityEntry(2, "apple", 20, Axis(0)));

        for (int i = 0; i < 5; i++)
        {
            index.Nearest(Axis(0), count: 1, similarityFloor: 0)[0].Label.ShouldBe("apple");
        }
    }

    /// <summary>
    /// The measured operating point. Two unrelated merchants embed around 0.4, so the floor
    /// has to reject that while accepting a genuine match.
    /// </summary>
    [Fact]
    public void The_default_floor_rejects_the_distance_unrelated_merchants_sit_at()
    {
        SimilarityIndex index = Index(
            new SimilarityEntry(1, "unrelated", 10, Between(0, 1, Math.Sqrt(1 - (0.42 * 0.42)))));

        index.Predict(Axis(0)).ShouldBeNull();

        SimilarityIndex close = Index(
            new SimilarityEntry(1, "same kind", 10, Between(0, 1, Math.Sqrt(1 - (0.88 * 0.88)))));

        close.Predict(Axis(0)).ShouldNotBeNull();
    }
}

/// <summary>
/// The two sources added below the classifier, and the order they sit in.
/// </summary>
/// <remarks>
/// Uses hand-made vectors rather than a model, so what is being tested is the chain's
/// behaviour and not the embedder's quality.
/// </remarks>
public sealed class SuggesterFallbackTests
{
    private static float[] Axis(int index, int size = 4)
    {
        var v = new float[size];
        v[index] = 1;
        return v;
    }

    private static CategorySuggester.SuggestionInput Input(
        float[]? vector = null,
        string? merchantCode = null,
        int? payeeLastCategoryId = null,
        int? fileCategoryId = null) =>
        new(
            AccountId: 1,
            PayeeName: "SOMEWHERE NEW",
            Descriptor: "SOMEWHERE NEW",
            Amount: Money.FromDecimal(-10m),
            FileCategoryId: fileCategoryId,
            PayeeLastCategoryId: payeeLastCategoryId,
            Vector: vector,
            MerchantCode: merchantCode);

    private static SimilarityIndex Grocers() => SimilarityIndex.Build(
        [new SimilarityEntry(1, "FABRIKAM FOODS MKT", 42, Axis(0))]);

    [Fact]
    public void A_close_merchant_supplies_a_category_when_nothing_else_can()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(vector: Axis(0)), [], CategoryClassifier.Empty, similar: Grocers());

        suggestion.Source.ShouldBe(SuggestionSource.Similar);
        suggestion.CategoryId.ShouldBe(42);
    }

    /// <summary>A suggestion the user cannot see the reasoning behind is one they must redo.</summary>
    [Fact]
    public void The_similarity_suggestion_says_which_merchant_it_matched()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(vector: Axis(0)), [], CategoryClassifier.Empty, similar: Grocers());

        suggestion.SimilarTo.ShouldBe("FABRIKAM FOODS MKT");
        suggestion.Describe().ShouldBe("Looks like FABRIKAM FOODS MKT");
    }

    [Fact]
    public void A_distant_merchant_is_not_reported_at_all()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(vector: Axis(1)), [], CategoryClassifier.Empty, similar: Grocers());

        suggestion.Source.ShouldBe(SuggestionSource.None);
    }

    /// <summary>
    /// The whole feature is optional: with no vector the chain must behave exactly as it did
    /// before any of this existed.
    /// </summary>
    [Fact]
    public void With_no_vector_the_chain_is_unchanged()
    {
        CategorySuggester.Suggest(Input(), [], CategoryClassifier.Empty, similar: Grocers())
            .Source.ShouldBe(SuggestionSource.None);
    }

    [Fact]
    public void What_the_payee_was_last_filed_under_beats_a_lookalike()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(vector: Axis(0), payeeLastCategoryId: 7), [], CategoryClassifier.Empty, similar: Grocers());

        suggestion.Source.ShouldBe(SuggestionSource.PayeeMemory);
        suggestion.CategoryId.ShouldBe(7);
    }

    [Fact]
    public void A_merchant_code_is_used_when_nothing_else_answers()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(merchantCode: "5411"),
            [],
            CategoryClassifier.Empty,
            merchantCodes: new Dictionary<string, int> { ["5411"] = 99 });

        suggestion.Source.ShouldBe(SuggestionSource.Sic);
        suggestion.CategoryId.ShouldBe(99);
        suggestion.Describe().ShouldBe("From the merchant's industry code");
    }

    /// <summary>
    /// A code says what trade the merchant is in; a lookalike says how this person files
    /// that kind of shop. The second is closer to a decision the user actually made.
    /// </summary>
    [Fact]
    public void A_lookalike_from_the_users_own_history_beats_a_generic_industry_code()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(vector: Axis(0), merchantCode: "5411"),
            [],
            CategoryClassifier.Empty,
            similar: Grocers(),
            merchantCodes: new Dictionary<string, int> { ["5411"] = 99 });

        suggestion.Source.ShouldBe(SuggestionSource.Similar);
        suggestion.CategoryId.ShouldBe(42);
    }

    [Fact]
    public void An_unknown_merchant_code_is_not_invented_into_a_category()
    {
        CategorySuggester.Suggest(
            Input(merchantCode: "0000"),
            [],
            CategoryClassifier.Empty,
            merchantCodes: new Dictionary<string, int> { ["5411"] = 99 })
            .Source.ShouldBe(SuggestionSource.None);
    }
}

/// <summary>
/// The line between filling a category in and merely offering it.
/// </summary>
/// <remarks>
/// Lives on the suggestion rather than in the dialog, so the rule is the same everywhere and
/// can be checked without a window. Getting it wrong in the permissive direction means a 63%
/// guess silently written onto a saved transaction.
/// </remarks>
public sealed class SuggestionCertaintyTests
{
    private static CategorySuggestion From(SuggestionSource source) => new()
    {
        CategoryId = 1,
        Source = source,
        Confidence = 1,
    };

    [Theory]
    [InlineData(SuggestionSource.Rule)]
    [InlineData(SuggestionSource.FileCategory)]
    [InlineData(SuggestionSource.PayeeMemory)]
    public void What_the_user_decided_can_be_applied_without_asking(SuggestionSource source)
    {
        From(source).IsCertain.ShouldBeTrue();
    }

    [Theory]
    [InlineData(SuggestionSource.Statistical)]
    [InlineData(SuggestionSource.Similar)]
    [InlineData(SuggestionSource.Sic)]
    public void What_was_inferred_is_only_ever_offered(SuggestionSource source)
    {
        From(source).IsCertain.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_suggested_is_not_a_certainty_either()
    {
        CategorySuggestion.None.IsCertain.ShouldBeFalse();
        CategorySuggestion.None.HasCategory.ShouldBeFalse();
    }
}
