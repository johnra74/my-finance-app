using MyFinance.Import.Categorization;

namespace MyFinance.Import.Tests.Categorization;

public sealed class VectorQuantizerTests
{
    private static float[] Unit(int size, int seed)
    {
        var random = new Random(seed);
        var v = new float[size];
        double sum = 0;

        for (int i = 0; i < size; i++)
        {
            v[i] = (float)((random.NextDouble() * 2) - 1);
            sum += v[i] * (double)v[i];
        }

        double magnitude = Math.Sqrt(sum);

        for (int i = 0; i < size; i++)
        {
            v[i] = (float)(v[i] / magnitude);
        }

        return v;
    }

    private static double Dot(float[] a, float[] b)
    {
        double d = 0;

        for (int i = 0; i < a.Length; i++)
        {
            d += a[i] * (double)b[i];
        }

        return d;
    }

    [Fact]
    public void A_vector_survives_the_round_trip_intact_enough_to_rank_with()
    {
        float[] original = Unit(384, seed: 7);

        float[] restored = VectorQuantizer.Unpack(VectorQuantizer.Pack(original));

        restored.Length.ShouldBe(384);

        // The cosine is what the index actually compares, so that is what must survive.
        Dot(original, restored).ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void Packing_costs_one_byte_a_dimension_plus_a_small_header()
    {
        VectorQuantizer.Pack(Unit(384, seed: 1)).Length.ShouldBe(384 + sizeof(float));
    }

    [Fact]
    public void Relative_distances_are_preserved()
    {
        float[] query = Unit(384, seed: 1);
        float[] near = Unit(384, seed: 1);
        float[] far = Unit(384, seed: 99);

        float[] restoredNear = VectorQuantizer.Unpack(VectorQuantizer.Pack(near));
        float[] restoredFar = VectorQuantizer.Unpack(VectorQuantizer.Pack(far));

        Dot(query, restoredNear).ShouldBeGreaterThan(Dot(query, restoredFar));
    }

    [Fact]
    public void An_empty_or_missing_vector_round_trips_to_nothing()
    {
        VectorQuantizer.Pack([]).ShouldBeEmpty();
        VectorQuantizer.Unpack(null).ShouldBeEmpty();
        VectorQuantizer.Unpack([]).ShouldBeEmpty();
        VectorQuantizer.Unpack([1, 2]).ShouldBeEmpty();
    }

    [Fact]
    public void A_zero_vector_does_not_divide_by_its_own_length()
    {
        float[] restored = VectorQuantizer.Unpack(VectorQuantizer.Pack(new float[384]));

        restored.Length.ShouldBe(384);
        restored.ShouldAllBe(v => v == 0);
    }
}
