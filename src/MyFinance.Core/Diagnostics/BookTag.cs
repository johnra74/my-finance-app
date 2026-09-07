using System.Security.Cryptography;
using System.Text;

namespace MyFinance.Core.Diagnostics;

/// <summary>
/// A short, stable, non-reversible name for a book.
/// </summary>
/// <remarks>
/// <para>
/// Entries need to say <em>which</em> book they came from — a fault that only happens in one
/// of two books is a different fault from one that happens in both — without saying anything
/// about it. Neither the path, which usually contains the user's real name, nor the file name,
/// which they chose and which can be as revealing as the contents: a book called
/// "Divorce settlement" is sensitive before it is opened.
/// </para>
/// <para>
/// So: eight hex characters of a SHA-256 over the full path. Stable across sessions, different
/// for different books, and nothing can be read back out of it. Eight characters is short
/// enough to be readable in a log and far more than enough to tell one person's two or three
/// books apart — this is a label, not a security boundary.
/// </para>
/// </remarks>
public static class BookTag
{
    /// <summary>What is written when no book is open.</summary>
    public const string None = "no-book";

    public static string For(string? databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return None;
        }

        // The full path, so two books of the same name in different folders differ. Casing is
        // normalised because Windows would otherwise tag the same book two ways.
        string normalised = databasePath.Trim().ToUpperInvariant();

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));

        return Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }
}
