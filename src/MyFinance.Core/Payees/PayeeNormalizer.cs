using System.Globalization;
using System.Text;

namespace MyFinance.Core.Payees;

/// <summary>
/// Reduces a payee name to a canonical form used for matching.
/// </summary>
/// <remarks>
/// This is the conservative half of payee handling: case, punctuation and whitespace only,
/// so "Blue Bottle Coffee" and "BLUE BOTTLE  COFFEE." collapse together. Stripping the
/// noise real banks add — <c>SQ *</c> prefixes, store numbers, trailing city and state —
/// belongs to the importer, which has the surrounding context needed to do it safely.
/// </remarks>
public static class PayeeNormalizer
{
    /// <summary>
    /// Upper-cases, drops everything that is not a letter or digit, and collapses runs of
    /// whitespace to a single space.
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        bool pendingSpace = false;

        foreach (char character in name.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(char.ToUpper(character, CultureInfo.CurrentCulture));
            }
            else
            {
                // Punctuation separates just as whitespace does, so "AT&T" and "AT T" match.
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>True when two names refer to the same payee once normalized.</summary>
    public static bool AreEquivalent(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
}
