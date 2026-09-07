using MyFinance.Import.Categorization;

namespace MyFinance.Semantics.Tests;

/// <summary>
/// Checks the embedder against the behaviour the suggestion pipeline depends on.
/// </summary>
/// <remarks>
/// A neural model cannot be asserted the way arithmetic can, so these pin the properties
/// that actually matter downstream — unit length, batch ordering, and that merchants of the
/// same kind sit closer together than merchants of different kinds. A pooling mistake or a
/// swapped model breaks those loudly rather than quietly making worse suggestions.
/// </remarks>
public sealed class OnnxTextEmbedderTests : IDisposable
{
    private readonly OnnxTextEmbedder _embedder = OnnxTextEmbedder.Create();

    public void Dispose() => _embedder.Dispose();

    private static float Cosine(float[] a, float[] b)
    {
        float dot = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }

    private float[] One(string text) => _embedder.Embed([text])[0];

    [Fact]
    public void The_model_loads()
    {
        _embedder.IsAvailable.ShouldBeTrue(
            $"the model should be embedded in this build. {_embedder.Failure}");
    }

    [Fact]
    public void A_vector_is_the_right_width_finite_and_of_unit_length()
    {
        float[] vector = One("BLUE BOTTLE COFFEE NEW YORK NY");

        vector.Length.ShouldBe(OnnxTextEmbedder.VectorSize);
        vector.ShouldAllBe(v => float.IsFinite(v));

        // Normalised at the source, so every cosine downstream is a plain dot product.
        Cosine(vector, vector).ShouldBe(1f, 0.001f);
    }

    [Fact]
    public void The_same_text_embeds_the_same_way_every_time()
    {
        Cosine(One("FABRIKAM GAS STATION 5772"), One("FABRIKAM GAS STATION 5772")).ShouldBe(1f, 0.0001f);
    }

    /// <summary>
    /// The property the whole feature rests on: two grocers must sit closer together than a
    /// grocer and a petrol station, with no token in common between any two of them doing the
    /// work. The names are invented, so what the model is being asked to recognise is the
    /// kind of shop rather than a brand it happens to have memorised.
    /// </summary>
    [Fact]
    public void Merchants_of_a_kind_sit_closer_than_merchants_of_different_kinds()
    {
        float[] oneGrocer = One("CONTOSO SUPERMARKET #543");
        float[] anotherGrocer = One("NORTHWIND GROCERY STORE 10442");
        float[] fuel = One("FABRIKAM GAS STATION 5772");

        float sameKind = Cosine(oneGrocer, anotherGrocer);
        float differentKind = Cosine(oneGrocer, fuel);

        sameKind.ShouldBeGreaterThan(differentKind);
    }

    [Fact]
    public void A_batch_comes_back_in_the_order_it_was_given()
    {
        string[] texts = ["FABRIKAM FUEL 5772", "CONTOSO MARKET #543", "PROSEWARE.COM"];

        IReadOnlyList<float[]> batch = _embedder.Embed(texts);

        batch.Count.ShouldBe(3);

        for (int i = 0; i < texts.Length; i++)
        {
            Cosine(batch[i], One(texts[i])).ShouldBe(1f, 0.0001f);
        }
    }

    /// <summary>
    /// Padding must not reach the pooled average, or a short text batched with a long one
    /// would embed differently from the same text on its own.
    /// </summary>
    [Fact]
    public void Padding_in_a_batch_does_not_change_a_short_text()
    {
        const string Short = "CVS";
        string longer = string.Join(" ", Enumerable.Repeat("PURCHASE AUTHORIZED ON MERCHANT", 6));

        float[] alone = One(Short);
        float[] batched = _embedder.Embed([Short, longer])[0];

        Cosine(alone, batched).ShouldBe(1f, 0.0001f);
    }

    [Fact]
    public void Empty_input_is_answered_with_nothing_rather_than_a_throw()
    {
        _embedder.Embed([]).ShouldBeEmpty();
    }

    [Fact]
    public void Blank_text_still_produces_a_usable_vector()
    {
        float[] vector = One("   ");

        vector.Length.ShouldBe(OnnxTextEmbedder.VectorSize);
        vector.ShouldAllBe(v => float.IsFinite(v));
    }

    /// <summary>The whole feature is optional, and the null path has to stay silent.</summary>
    [Fact]
    public void The_null_embedder_is_unavailable_and_answers_nothing()
    {
        ITextEmbedder none = NullTextEmbedder.Instance;

        none.IsAvailable.ShouldBeFalse();
        none.Embed(["anything"]).ShouldBeEmpty();
    }
}
