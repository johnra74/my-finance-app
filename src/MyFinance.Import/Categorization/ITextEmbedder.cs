namespace MyFinance.Import.Categorization;

/// <summary>
/// Turns transaction text into a vector whose direction carries its meaning.
/// </summary>
/// <remarks>
/// <para>
/// Declared here, with no dependency on anything that can produce one, so this project stays
/// pure and its tests stay fast. The implementation that loads a neural model lives in
/// <c>MyFinance.Semantics</c>, and nothing in the categorization pipeline knows or cares
/// which implementation it has.
/// </para>
/// <para>
/// Vectors are expected to be L2-normalised, so a cosine similarity is a dot product and
/// callers never have to remember to divide.
/// </para>
/// </remarks>
public interface ITextEmbedder
{
    /// <summary>Length of the vectors this embedder produces.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Whether this embedder can actually do anything.
    /// </summary>
    /// <remarks>
    /// False when the model or its native runtime could not be loaded. Callers check this
    /// rather than catching: a machine that cannot load the model should quietly lose one
    /// suggestion source, not fail an import.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// Embeds a batch of texts, returning one unit vector each, in the order given.
    /// </summary>
    /// <remarks>
    /// A batch rather than one at a time because the cost is dominated by the forward pass,
    /// and embedding four thousand payees one call at a time is minutes rather than seconds.
    /// </remarks>
    IReadOnlyList<float[]> Embed(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>
/// An embedder that does nothing, for when no model is present.
/// </summary>
/// <remarks>
/// The whole similarity feature is optional by construction. This is what makes that true:
/// with it in place the suggestion chain falls back to exactly the four sources it had
/// before, and no caller needs a null check.
/// </remarks>
public sealed class NullTextEmbedder : ITextEmbedder
{
    public static NullTextEmbedder Instance { get; } = new();

    public int Dimensions => 0;

    public bool IsAvailable => false;

    public IReadOnlyList<float[]> Embed(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) => [];
}
