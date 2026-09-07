using MyFinance.Core.Payees;

namespace MyFinance.Import.Payees;

/// <summary>A payee already in the book, as the importer needs to see it.</summary>
/// <param name="Id">Database id.</param>
/// <param name="Name">Display name.</param>
/// <param name="NormalizedName">Its canonical form, for matching.</param>
/// <param name="LastCategoryId">The category it was last used with, if any.</param>
public sealed record KnownPayee(int Id, string Name, string NormalizedName, int? LastCategoryId);

/// <summary>A descriptor the user has already mapped onto a payee.</summary>
/// <param name="PayeeId">The payee it resolves to.</param>
/// <param name="NormalizedPattern">The stable key it was recorded under.</param>
public sealed record KnownAlias(int PayeeId, string NormalizedPattern);

/// <summary>How a descriptor was resolved, which the preview shows so the user can audit it.</summary>
public enum PayeeMatchKind
{
    /// <summary>A correction the user made earlier. The strongest signal there is.</summary>
    Alias = 0,

    /// <summary>The bank's raw text matches an existing payee's name.</summary>
    ExactRaw = 1,

    /// <summary>The tidied name matches an existing payee.</summary>
    ExactCleaned = 2,

    /// <summary>Nothing matched; a payee will be created.</summary>
    New = 3,
}

/// <summary>What a descriptor resolved to.</summary>
public sealed record PayeeMatch
{
    public int? PayeeId { get; init; }

    public required string Name { get; init; }

    public required PayeeMatchKind Kind { get; init; }

    /// <summary>The matched payee's remembered category, which pre-fills the row.</summary>
    public int? LastCategoryId { get; init; }

    public bool IsNew => Kind == PayeeMatchKind.New;
}

/// <summary>
/// A snapshot of the book's payees, loaded once per import.
/// </summary>
/// <remarks>
/// Matching several hundred rows against the database one query at a time is what makes an
/// import feel slow; two dictionaries built up front turn it into as many lookups.
/// </remarks>
public sealed class PayeeIndex
{
    private readonly Dictionary<string, KnownPayee> _byNormalizedName;
    private readonly Dictionary<string, int> _byAlias;
    private readonly Dictionary<int, KnownPayee> _byId;

    public PayeeIndex(IEnumerable<KnownPayee> payees, IEnumerable<KnownAlias> aliases)
    {
        ArgumentNullException.ThrowIfNull(payees);
        ArgumentNullException.ThrowIfNull(aliases);

        _byId = [];
        _byNormalizedName = new Dictionary<string, KnownPayee>(StringComparer.Ordinal);

        foreach (KnownPayee payee in payees)
        {
            _byId[payee.Id] = payee;
            _byNormalizedName.TryAdd(payee.NormalizedName, payee);
        }

        _byAlias = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (KnownAlias alias in aliases)
        {
            _byAlias.TryAdd(alias.NormalizedPattern, alias.PayeeId);
        }
    }

    public static PayeeIndex Empty { get; } = new([], []);

    public int Count => _byId.Count;

    public KnownPayee? ById(int id) => _byId.GetValueOrDefault(id);

    internal KnownPayee? ByNormalizedName(string normalized) =>
        _byNormalizedName.GetValueOrDefault(normalized);

    internal KnownPayee? ByAlias(string stableKey) =>
        _byAlias.TryGetValue(stableKey, out int id) ? _byId.GetValueOrDefault(id) : null;
}

/// <summary>
/// Decides which payee a bank descriptor belongs to.
/// </summary>
/// <remarks>
/// Strictly ordered, with a recorded correction winning over anything this code could infer.
/// Where nothing matches, a new payee is proposed rather than a doubtful existing one being
/// reused — a wrong match is invisible once committed, whereas a duplicate payee is obvious
/// and can be merged in one action.
/// </remarks>
public static class PayeeMatcher
{
    public static PayeeMatch Match(CleanedDescriptor descriptor, PayeeIndex index)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(index);

        // An alias is something the user told us explicitly. It outranks every heuristic.
        if (descriptor.StableKey.Length > 0 && index.ByAlias(descriptor.StableKey) is KnownPayee aliased)
        {
            return new PayeeMatch
            {
                PayeeId = aliased.Id,
                Name = aliased.Name,
                Kind = PayeeMatchKind.Alias,
                LastCategoryId = aliased.LastCategoryId,
            };
        }

        string normalizedRaw = PayeeNormalizer.Normalize(descriptor.Raw);

        if (normalizedRaw.Length > 0 && index.ByNormalizedName(normalizedRaw) is KnownPayee rawHit)
        {
            return new PayeeMatch
            {
                PayeeId = rawHit.Id,
                Name = rawHit.Name,
                Kind = PayeeMatchKind.ExactRaw,
                LastCategoryId = rawHit.LastCategoryId,
            };
        }

        string normalizedClean = PayeeNormalizer.Normalize(descriptor.Suggested);

        if (normalizedClean.Length > 0 && index.ByNormalizedName(normalizedClean) is KnownPayee cleanHit)
        {
            return new PayeeMatch
            {
                PayeeId = cleanHit.Id,
                Name = cleanHit.Name,
                Kind = PayeeMatchKind.ExactCleaned,
                LastCategoryId = cleanHit.LastCategoryId,
            };
        }

        return new PayeeMatch
        {
            PayeeId = null,
            Name = descriptor.Suggested,
            Kind = PayeeMatchKind.New,
        };
    }
}
