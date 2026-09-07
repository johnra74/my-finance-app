using System.Globalization;
using System.Text;
using MyFinance.Core.Payees;

namespace MyFinance.Import.Payees;

/// <summary>The result of tidying one bank descriptor.</summary>
public sealed record CleanedDescriptor
{
    /// <summary>Exactly what the bank wrote. Never discarded; it goes into the memo.</summary>
    public required string Raw { get; init; }

    /// <summary>The name to offer as the payee.</summary>
    public required string Suggested { get; init; }

    /// <summary>
    /// The key an alias is recorded under, so a correction sticks across future downloads.
    /// </summary>
    /// <remarks>
    /// Only the parts that change between visits are removed — store numbers, reference
    /// codes, dates. Everything stable is kept, so two visits to the same merchant produce
    /// the same key even though their raw descriptors differ.
    /// </remarks>
    public required string StableKey { get; init; }

    /// <summary>Other readings, offered in the picker but never chosen automatically.</summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];

    /// <summary>Which rules fired, so a surprising result can be explained and tested.</summary>
    public IReadOnlyList<string> AppliedRules { get; init; } = [];
}

/// <summary>
/// Turns a bank descriptor into something worth showing as a payee name.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately cautious. Over-cleaning is the expensive failure: merging two genuinely
/// different merchants is invisible afterwards and corrupts every report that groups by
/// payee, whereas leaving noise in a name is a cosmetic annoyance the user fixes once — and
/// the alias makes that fix permanent.
/// </para>
/// <para>
/// Where a more aggressive reading is plausible it is offered in
/// <see cref="CleanedDescriptor.Alternatives"/> rather than applied.
/// </para>
/// </remarks>
public static class DescriptorCleaner
{
    /// <summary>Below this, a cleaning stage is assumed to have eaten the name itself.</summary>
    private const int MinimumUsefulLength = 3;

    public static CleanedDescriptor Clean(string? raw)
    {
        string original = Collapse(raw ?? string.Empty);

        if (original.Length == 0)
        {
            return new CleanedDescriptor { Raw = string.Empty, Suggested = string.Empty, StableKey = string.Empty };
        }

        var applied = new List<string>();

        // Volatile parts come off first and are also what the alias key drops, so the key
        // and the display name agree about what varies between visits.
        string stable = StripVolatile(original, applied);

        string working = stable;
        working = StripLeadingNoise(working, applied);

        string withLocation = working;
        working = StripTrailingRegion(working, applied);

        string suggested = Recase(working, applied);

        var alternatives = new List<string>();

        // The city is left in place: "Blue Bottle New York" and "Blue Bottle San Francisco"
        // are the same brand but not obviously the same payee, and which the user wants is
        // their call, not a guess this code should make on their behalf.
        if (!string.Equals(withLocation, working, StringComparison.Ordinal))
        {
            alternatives.Add(Recase(withLocation, []));
        }

        if (!string.Equals(original, suggested, StringComparison.OrdinalIgnoreCase))
        {
            alternatives.Add(original);
        }

        return new CleanedDescriptor
        {
            Raw = original,
            Suggested = suggested.Length >= MinimumUsefulLength ? suggested : original,
            StableKey = PayeeNormalizer.Normalize(stable),
            Alternatives = [.. alternatives.Distinct(StringComparer.OrdinalIgnoreCase)],
            AppliedRules = applied,
        };
    }

    /// <summary>
    /// Removes the parts of a descriptor that differ between two visits to the same merchant.
    /// </summary>
    /// <remarks>
    /// This is what makes an alias durable. Recording a correction against the raw text would
    /// key it to one transaction's store number, so it would never match again and the alias
    /// table would gain a dead row per import while never learning anything.
    /// </remarks>
    private static string StripVolatile(string text, List<string> applied)
    {
        // Multi-token patterns have to come off before the string is split up.
        string current = Apply(text, DescriptorRules.TrailingPhone, "phone", applied);
        current = Apply(current, DescriptorRules.TrailingCardTail, "card-tail", applied);

        string[] tokens = current.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 1)
        {
            return current;
        }

        var kept = new List<string>(tokens.Length) { tokens[0] };

        // Position-independent, not just trailing. A store number is very often followed by
        // the branch location — "SQ *BLUE BOTTLE 1234 NEW YORK NY" — and a trailing-only rule
        // would leave the digits in, which is precisely what would make the alias key differ
        // between two visits to the same shop and stop corrections ever sticking.
        for (int i = 1; i < tokens.Length; i++)
        {
            if (IsVolatileToken(tokens[i]))
            {
                applied.Add("volatile:" + tokens[i]);
                continue;
            }

            kept.Add(tokens[i]);
        }

        string result = string.Join(' ', kept);
        return result.Length >= MinimumUsefulLength ? result : current;
    }

    /// <summary>
    /// Whether a single token is a reference that changes between visits.
    /// </summary>
    /// <remarks>
    /// Never applied to the first token, so "7 ELEVEN" and "76" keep the number that is
    /// their name. Runs of one or two digits are also left alone — "STUDIO 54", "PIER 1" —
    /// while three or more are treated as a branch number, because three-digit store codes
    /// are common ("CONTOSO MARKET 553") and collapsing two branches of one chain into a single
    /// payee is what the user wants anyway.
    /// </remarks>
    private static bool IsVolatileToken(string token)
    {
        string bare = token.TrimStart('#');

        if (bare.Length == 0)
        {
            return false;
        }

        // "#12" is explicitly marked as a number by the bank, however short.
        if (token.StartsWith('#') && bare.All(char.IsAsciiDigit))
        {
            return true;
        }

        if (bare.Length >= 3 && bare.All(char.IsAsciiDigit))
        {
            return true;
        }

        if (DescriptorRules.TokenDate.IsMatch(token) || DescriptorRules.TokenCardTail.IsMatch(token))
        {
            return true;
        }

        return DescriptorRules.TokenReference.IsMatch(token);
    }

    private static string StripLeadingNoise(string text, List<string> applied)
    {
        string current = text;

        current = Apply(current, DescriptorRules.CheckCardPrefix, "checkcard-prefix", applied);

        foreach (string prefix in DescriptorRules.TransactionKindPrefixes)
        {
            if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string candidate = current[prefix.Length..].TrimStart(' ', '-', ':');
                if (candidate.Length >= MinimumUsefulLength)
                {
                    applied.Add("kind-prefix:" + prefix);
                    current = candidate;
                }

                break;
            }
        }

        // Processors that use the asterisk form, handled generically so an unfamiliar one
        // still works.
        string stripped = DescriptorRules.ProcessorPrefix.Replace(current, string.Empty, 1);
        if (!string.Equals(stripped, current, StringComparison.Ordinal)
            && stripped.Length >= MinimumUsefulLength)
        {
            applied.Add("processor-prefix");
            current = stripped;
        }

        foreach (string prefix in DescriptorRules.ProcessorPrefixes)
        {
            if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string candidate = current[prefix.Length..].TrimStart();
                if (candidate.Length >= MinimumUsefulLength)
                {
                    applied.Add("processor:" + prefix.Trim());
                    current = candidate;
                }

                break;
            }
        }

        return current.Trim();
    }

    /// <summary>
    /// Drops a trailing state or province code, and a trailing "USA".
    /// </summary>
    /// <remarks>
    /// Matched against a closed list rather than "any two capitals", so a merchant whose name
    /// genuinely ends in two letters is left alone.
    /// </remarks>
    private static string StripTrailingRegion(string text, List<string> applied)
    {
        string current = text;

        for (int pass = 0; pass < 2; pass++)
        {
            int lastSpace = current.LastIndexOf(' ');
            if (lastSpace <= 0)
            {
                break;
            }

            string tail = current[(lastSpace + 1)..];
            bool isRegion = tail.Length == 2 && DescriptorRules.RegionCodes.Contains(tail.ToUpperInvariant());
            bool isCountry = tail.Equals("USA", StringComparison.OrdinalIgnoreCase);

            if (!isRegion && !isCountry)
            {
                break;
            }

            string candidate = current[..lastSpace].TrimEnd();
            if (candidate.Length < MinimumUsefulLength)
            {
                break;
            }

            applied.Add(isCountry ? "country" : "region:" + tail);
            current = candidate;
        }

        return current;
    }

    /// <summary>
    /// Title-cases a name the bank shouted, and leaves alone one it did not.
    /// </summary>
    /// <remarks>
    /// Mixed-case input means the bank already formatted the name, so re-casing it could only
    /// make it worse. No special handling of "Mc" or "Mac": turning MCDONALDS into Mcdonalds
    /// is mildly ugly, whereas turning MACHINE into MacHine is a bug.
    /// </remarks>
    private static string Recase(string text, List<string> applied)
    {
        int letters = text.Count(char.IsLetter);
        if (letters == 0)
        {
            return text;
        }

        int upper = text.Count(char.IsUpper);
        if (upper * 100 < letters * 80)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(RecaseToken(tokens[i], isFirst: i == 0));
        }

        applied.Add("recase");
        return builder.ToString();
    }

    private static string RecaseToken(string token, bool isFirst)
    {
        if (DescriptorRules.KeepUpper.Contains(token.ToUpperInvariant()))
        {
            return token.ToUpperInvariant();
        }

        // Anything with a digit in it is a name the bank chose deliberately — 7-ELEVEN, 76,
        // 99 RANCH — and re-casing it only risks damage.
        if (token.Any(char.IsDigit))
        {
            return token;
        }

        // A short run with no vowels is almost always an acronym: CVS, BP, TJX, LLC.
        if (token.Length <= 3 && !token.Any(c => "AEIOUYaeiouy".Contains(c, StringComparison.Ordinal)))
        {
            return token.ToUpperInvariant();
        }

        if (!isFirst && DescriptorRules.Connectives.Contains(token))
        {
            return token.ToLower(CultureInfo.CurrentCulture);
        }

        return string.Concat(
            char.ToUpper(token[0], CultureInfo.CurrentCulture),
            token[1..].ToLower(CultureInfo.CurrentCulture));
    }

    private static string Apply(
        string text,
        System.Text.RegularExpressions.Regex pattern,
        string ruleName,
        List<string> applied)
    {
        string result = pattern.Replace(text, string.Empty, 1).TrimEnd();

        // A rule that would consume the whole name has misfired; keep what we had.
        if (string.Equals(result, text, StringComparison.Ordinal) || result.Length < MinimumUsefulLength)
        {
            return text;
        }

        applied.Add(ruleName);
        return result;
    }

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;

        foreach (char character in text)
        {
            // Non-breaking spaces and control characters arrive in real descriptors and would
            // otherwise become part of a payee name.
            if (char.IsWhiteSpace(character) || char.IsControl(character) || character == ' ')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
