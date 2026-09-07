namespace MyFinance.Core.Entities;

/// <summary>
/// A payee's cached embedding, so the model is not re-run on text that has not changed.
/// </summary>
/// <remarks>
/// Embedding four thousand payees takes around twenty-five seconds. Doing that on every
/// import would be unusable, so the vectors live in the book and are refreshed only when the
/// text behind them changes — which is what <see cref="TextHash" /> is for.
/// </remarks>
public class PayeeEmbedding
{
    public int Id { get; set; }

    public int PayeeId { get; set; }

    public Payee? Payee { get; set; }

    /// <summary>
    /// Hash of the text that was embedded.
    /// </summary>
    /// <remarks>
    /// A rename has to invalidate the vector, and comparing hashes is cheaper than storing
    /// and comparing the text a second time.
    /// </remarks>
    public required string TextHash { get; set; }

    /// <summary>The vector, quantised to a byte per dimension.</summary>
    public required byte[] Vector { get; set; }

    /// <summary>Which model produced it, so a model change invalidates the whole cache.</summary>
    public required string Model { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
