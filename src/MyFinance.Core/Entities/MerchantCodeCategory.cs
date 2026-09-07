namespace MyFinance.Core.Entities;

/// <summary>
/// Maps a merchant industry code to the category it usually belongs in.
/// </summary>
/// <remarks>
/// <para>
/// Banks optionally stamp a Standard Industrial Classification code on a downloaded
/// transaction, saying what line of business the merchant is in. Where one is present it is a
/// fact about the merchant rather than a guess from its name, which makes it worth more than
/// any inference — but it maps to a <em>general</em> category, not necessarily the one this
/// particular person files that merchant under, which is why it sits last in the chain.
/// </para>
/// <para>
/// The starting set is brought across from Microsoft Money, which shipped a curated table of
/// them. Rows can be edited or added afterwards like anything else in the book.
/// </para>
/// </remarks>
public class MerchantCodeCategory
{
    public int Id { get; set; }

    /// <summary>The SIC code, as text so leading zeros survive.</summary>
    public required string Code { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }
}
