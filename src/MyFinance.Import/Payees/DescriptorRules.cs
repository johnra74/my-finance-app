using System.Text.RegularExpressions;

namespace MyFinance.Import.Payees;

/// <summary>
/// The lookup tables and patterns <see cref="DescriptorCleaner"/> works from.
/// </summary>
/// <remarks>
/// Kept apart from the algorithm so the lists can grow as new banks and processors turn up
/// without anyone having to re-reason about the pipeline itself.
/// </remarks>
internal static partial class DescriptorRules
{
    /// <summary>
    /// How a bank labels the kind of transaction, before the merchant's own name starts.
    /// Longest first, so "POS DEBIT" is not partly consumed by "POS".
    /// </summary>
    public static readonly string[] TransactionKindPrefixes =
    [
        "PREAUTHORIZED DEBIT",
        "DEBIT CARD PURCHASE",
        "RECURRING PAYMENT",
        "EXTERNAL WITHDRAWAL",
        "PREAUTH DEBIT",
        "DEBIT PURCHASE",
        "POS PURCHASE",
        "VISA DDA PUR",
        "ACH WITHDRAWAL",
        "CARD PURCHASE",
        "POS DEBIT",
        "ACH DEBIT",
        "ACH CREDIT",
        "WITHDRAWAL",
        "CHECKCARD",
        "PURCHASE",
    ];

    /// <summary>
    /// Payment processors that put themselves in front of the merchant. Only the ones that
    /// do not use the generic asterisk form need listing here.
    /// </summary>
    public static readonly string[] ProcessorPrefixes =
    [
        "PAYPAL ",
        "SQUARE ",
        "TOAST ",
    ];

    /// <summary>
    /// Words that stay lower case inside a title-cased name, unless they come first.
    /// </summary>
    public static readonly HashSet<string> Connectives =
        new(StringComparer.OrdinalIgnoreCase) { "of", "and", "the", "at", "for", "in", "on", "to", "de", "la" };

    /// <summary>
    /// Names that are acronyms or brands and must not be title-cased into nonsense.
    /// </summary>
    public static readonly HashSet<string> KeepUpper =
        new(StringComparer.Ordinal)
        {
            "ATM", "USA", "US", "UK", "LLC", "INC", "CO", "LTD", "PLC", "NYC", "TV", "IT",
            "HQ", "CVS", "BP", "KFC", "AMC", "IHOP", "UPS", "USPS", "TJX", "H&M", "AT&T",
            "IBM", "IKEA", "DMV", "IRS", "ATT", "PG&E", "T-MOBILE", "AAA", "BJS", "REI",
        };

    /// <summary>
    /// US states and territories plus Canadian provinces, for recognising a trailing
    /// location code. Matching against a closed list rather than "any two letters" is what
    /// stops a merchant genuinely ending in two letters from being truncated.
    /// </summary>
    public static readonly HashSet<string> RegionCodes =
        new(StringComparer.Ordinal)
        {
            "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID", "IL",
            "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT",
            "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI",
            "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
            "DC", "PR", "VI", "GU", "AS", "MP",
            "AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT",
        };

    /// <summary>
    /// The generic processor prefix: two to seven letters then an asterisk. Catches SQ*,
    /// TST*, PP*, PYPL*, WPY*, CLOVER*, SUMUP* and every processor not yet invented, which
    /// a fixed list never could.
    /// </summary>
    [GeneratedRegex(@"^[A-Z]{2,7}\s?\*\s?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex ProcessorPrefix { get; }

    /// <summary>A trailing store or reference number: "#1234", or four or more bare digits.</summary>
    [GeneratedRegex(@"\s+#\s?\d+$|\s+\d{4,}$", RegexOptions.CultureInvariant)]
    public static partial Regex TrailingStoreNumber { get; }

    /// <summary>A trailing reference token such as "REF000123456" or "TXN9876543".</summary>
    [GeneratedRegex(@"\s+[A-Z]{0,4}\d{6,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex TrailingReference { get; }

    /// <summary>A trailing masked card tail: "XXXX1234", "****5678", "ENDING IN 9012".</summary>
    [GeneratedRegex(
        @"\s+(ENDING\s+IN\s+\d{3,4}|X{2,}\d{3,4}|\*{2,}\d{3,4})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex TrailingCardTail { get; }

    /// <summary>A trailing bare date, which some banks append to every descriptor.</summary>
    [GeneratedRegex(@"\s+\d{1,2}/\d{1,2}(/\d{2,4})?$", RegexOptions.CultureInvariant)]
    public static partial Regex TrailingDate { get; }

    /// <summary>A trailing telephone number.</summary>
    [GeneratedRegex(
        @"\s+\+?1?[-. ]?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$",
        RegexOptions.CultureInvariant)]
    public static partial Regex TrailingPhone { get; }

    /// <summary>A single token that is a date, e.g. "03/14".</summary>
    [GeneratedRegex(@"^\d{1,2}/\d{1,2}(/\d{2,4})?$", RegexOptions.CultureInvariant)]
    public static partial Regex TokenDate { get; }

    /// <summary>A single token that masks a card number, e.g. "XXXX1234" or "****5678".</summary>
    [GeneratedRegex(@"^(X{2,}\d{3,4}|\*{2,}\d{3,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex TokenCardTail { get; }

    /// <summary>A single token that is a long reference code, e.g. "REF000123456".</summary>
    [GeneratedRegex(@"^[A-Z]{0,4}\d{6,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex TokenReference { get; }

    /// <summary>A leading "CHECKCARD 1234" style prefix, where the digits vary per card.</summary>
    [GeneratedRegex(@"^CHECKCARD\s+\d+\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex CheckCardPrefix { get; }
}
