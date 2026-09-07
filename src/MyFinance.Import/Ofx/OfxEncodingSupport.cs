using System.Text;

namespace MyFinance.Import.Ofx;

/// <summary>
/// Works out what encoding a statement file is in.
/// </summary>
/// <remarks>
/// Getting this wrong does not fail loudly — it produces merchant names with mojibake in
/// them, which then become payees, which then never match again. Real files declare
/// <c>CHARSET:1252</c>, or claim ASCII and ship a smart quote anyway.
/// </remarks>
public static class OfxEncodingSupport
{
    /// <summary>Windows-1252, the encoding OFX 1.x files actually use.</summary>
    public const int WindowsCodePage = 1252;

    /// <summary>
    /// Registers the code-page provider the first time anything here is touched.
    /// </summary>
    /// <remarks>
    /// A static constructor rather than something the host has to call at startup: every
    /// path that needs Windows-1252 goes through this class, so the registration cannot be
    /// missed, and tests, the WPF app and any future command-line tool all get it without
    /// an initialization step somebody has to remember.
    /// </remarks>
    static OfxEncodingSupport() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Windows-1252 where available, falling back to Latin-1.</summary>
    /// <remarks>
    /// The two differ only across 0x80–0x9F — which is exactly where the smart quotes, em
    /// dashes and euro signs banks emit live, so the fallback is a real degradation and not
    /// an equivalent. It exists only so a missing provider cannot stop an import.
    /// </remarks>
    public static Encoding WindowsLatin1
    {
        get
        {
            try
            {
                return Encoding.GetEncoding(
                    WindowsCodePage,
                    EncoderFallback.ReplacementFallback,
                    DecoderFallback.ReplacementFallback);
            }
            catch (NotSupportedException)
            {
                return Encoding.Latin1;
            }
            catch (ArgumentException)
            {
                return Encoding.Latin1;
            }
        }
    }

    /// <summary>
    /// Decodes the file, choosing an encoding from its byte-order mark, its XML declaration
    /// or its OFX header, in that order of authority.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        // A byte-order mark outranks every declaration in the file. Plenty of real files
        // carry a UTF-8 BOM under a header that flatly claims USASCII, and the BOM is right.
        if (TryReadBom(bytes, out Encoding? bomEncoding, out int bomLength))
        {
            return bomEncoding.GetString(bytes[bomLength..]);
        }

        // Latin-1 maps every byte to a character and never throws, so it is always safe for
        // a first look at the header — which is ASCII by definition in both OFX versions.
        string probe = Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, 2048)]);

        Encoding chosen = ChooseFromDeclarations(probe);
        return chosen.GetString(bytes);
    }

    private static Encoding ChooseFromDeclarations(string probe)
    {
        string upper = probe.ToUpperInvariant();

        if (upper.TrimStart().StartsWith("<?XML", StringComparison.Ordinal))
        {
            // OFX 2.x is XML; honour its declaration, defaulting to UTF-8 as XML requires.
            string? declared = ReadAttribute(probe, "encoding");
            return ResolveNamed(declared) ?? Encoding.UTF8;
        }

        string? charset = ReadHeaderValue(upper, "CHARSET");
        string? encoding = ReadHeaderValue(upper, "ENCODING");

        if (encoding is "UTF-8" or "UTF8")
        {
            return Encoding.UTF8;
        }

        if (charset is not null && ResolveNamed(charset) is Encoding fromCharset)
        {
            return fromCharset;
        }

        // ENCODING:USASCII with CHARSET:NONE, or no header at all. Deliberately Windows-1252
        // rather than ASCII: 1252 is a strict superset, so this is lossless for genuinely
        // ASCII files and rescues the many that understate what they contain.
        return WindowsLatin1;
    }

    private static Encoding? ResolveNamed(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string trimmed = name.Trim().Trim('"', '\'');

        if (trimmed is "1252" or "CP1252" or "WINDOWS-1252" or "CP-1252")
        {
            return WindowsLatin1;
        }

        try
        {
            return Encoding.GetEncoding(
                trimmed,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryReadBom(ReadOnlySpan<byte> bytes, out Encoding encoding, out int length)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
            length = 3;
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        {
            encoding = Encoding.UTF32;
            length = 4;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            length = 2;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            length = 2;
            return true;
        }

        encoding = Encoding.UTF8;
        length = 0;
        return false;
    }

    /// <summary>Reads <c>KEY:VALUE</c> from the OFX 1.x header block.</summary>
    private static string? ReadHeaderValue(string upperProbe, string key)
    {
        foreach (string line in upperProbe.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            if (line.AsSpan(0, colon).Trim().SequenceEqual(key))
            {
                return line[(colon + 1)..].Trim();
            }
        }

        return null;
    }

    private static string? ReadAttribute(string text, string name)
    {
        int at = text.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        int start = at + name.Length + 1;
        if (start >= text.Length)
        {
            return null;
        }

        char quote = text[start];
        if (quote is not ('"' or '\''))
        {
            return null;
        }

        int end = text.IndexOf(quote, start + 1);
        return end < 0 ? null : text[(start + 1)..end];
    }
}
