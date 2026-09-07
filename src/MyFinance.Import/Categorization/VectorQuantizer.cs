namespace MyFinance.Import.Categorization;

/// <summary>
/// Packs a unit vector into bytes for storage, and unpacks it again.
/// </summary>
/// <remarks>
/// Four thousand payees at 384 dimensions is 6.3 MB as <see cref="float" />, against 1.6 MB
/// at one byte per dimension. The cost is a rounding error in the fourth decimal place of a
/// cosine, which is nothing next to the distances being compared; the saving is real, because
/// this lives inside the user's encrypted book and is rewritten whenever a payee is renamed.
/// </remarks>
public static class VectorQuantizer
{
    /// <summary>Bytes of header: the scale needed to reconstruct the values.</summary>
    private const int HeaderSize = sizeof(float);

    public static byte[] Pack(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        if (vector.Length == 0)
        {
            return [];
        }

        float largest = 0;

        foreach (float value in vector)
        {
            largest = Math.Max(largest, Math.Abs(value));
        }

        var packed = new byte[HeaderSize + vector.Length];
        BitConverter.TryWriteBytes(packed, largest);

        // A zero vector has no scale; leave the values at the midpoint rather than dividing.
        double scale = largest > 0 ? sbyte.MaxValue / (double)largest : 0;

        for (int i = 0; i < vector.Length; i++)
        {
            int q = (int)Math.Round(vector[i] * scale, MidpointRounding.AwayFromZero);
            packed[HeaderSize + i] = (byte)(sbyte)Math.Clamp(q, sbyte.MinValue, sbyte.MaxValue);
        }

        return packed;
    }

    public static float[] Unpack(byte[]? packed)
    {
        if (packed is null || packed.Length <= HeaderSize)
        {
            return [];
        }

        float largest = BitConverter.ToSingle(packed);
        int length = packed.Length - HeaderSize;
        var vector = new float[length];

        if (largest <= 0)
        {
            return vector;
        }

        double scale = largest / (double)sbyte.MaxValue;

        for (int i = 0; i < length; i++)
        {
            vector[i] = (float)((sbyte)packed[HeaderSize + i] * scale);
        }

        // Renormalised on the way out, so a dot product is still a cosine after the round trip.
        double sumOfSquares = 0;

        foreach (float value in vector)
        {
            sumOfSquares += value * (double)value;
        }

        double magnitude = Math.Sqrt(sumOfSquares);

        if (magnitude > 1e-9)
        {
            for (int i = 0; i < length; i++)
            {
                vector[i] = (float)(vector[i] / magnitude);
            }
        }

        return vector;
    }
}
