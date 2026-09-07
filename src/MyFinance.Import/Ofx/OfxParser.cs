using System.Text;
using MyFinance.Import.Model;

namespace MyFinance.Import.Ofx;

/// <summary>The file is not OFX at all, or is damaged past any useful reading.</summary>
public sealed class OfxParseException : Exception
{
    public OfxParseException(string message)
        : base(message)
    {
    }
}

/// <summary>The header block that precedes the document body.</summary>
/// <param name="MajorVersion">1 for SGML, 2 for XML.</param>
/// <param name="Values">Every declared key, upper-cased.</param>
public sealed record OfxHeader(int MajorVersion, IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string key) =>
        Values.TryGetValue(key.ToUpperInvariant(), out string? value) ? value : null;
}

/// <summary>A parsed statement file: its header, its element tree, and what went wrong.</summary>
public sealed record OfxDocument(
    OfxHeader Header,
    OfxNode Root,
    IReadOnlyList<ImportDiagnostic> Diagnostics);

/// <summary>
/// Reads an OFX file into an element tree.
/// </summary>
/// <remarks>
/// <para>
/// One code path serves both dialects. OFX 1.x is SGML in which a leaf's closing tag is
/// optional; OFX 2.x is XML, which simply always supplies it. Because the SGML rules are a
/// superset, a parser written for 1.x reads 2.x correctly by construction — whereas keeping
/// two implementations means two sets of bugs and a class of "works with one bank, fails
/// with another" faults that only reproduce against a file you do not have.
/// </para>
/// <para>
/// Whether a tag is a leaf or an aggregate is inferred structurally rather than from a list
/// of known tag names, because every vendor adds tags of its own and a fixed list turns each
/// one into a parse failure.
/// </para>
/// <para>
/// Nothing here throws for a merely malformed file. Real downloads arrive truncated, with
/// unbalanced tags and with junk between elements; a parser that gives up on the last row of
/// a four-hundred-row statement is worse than one that reports what it salvaged.
/// </para>
/// </remarks>
public static class OfxParser
{
    /// <summary>Root tag of every OFX document.</summary>
    public const string RootTag = "OFX";

    public static OfxDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Parse(OfxEncodingSupport.Decode(bytes));
    }

    public static OfxDocument ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static OfxDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var diagnostics = new List<ImportDiagnostic>();

        int bodyStart = text.IndexOf('<', StringComparison.Ordinal);
        if (bodyStart < 0)
        {
            throw new OfxParseException(
                "This file contains no markup, so it is not an OFX statement.");
        }

        OfxHeader header = ParseHeader(text[..bodyStart], text.AsSpan(bodyStart));
        OfxNode root = BuildTree(text.AsSpan(bodyStart), diagnostics);

        return new OfxDocument(header, root, diagnostics);
    }

    /// <summary>
    /// Reads the leading header block.
    /// </summary>
    /// <remarks>
    /// Everything before the first <c>&lt;</c> is the header, rather than "lines up to the
    /// first blank one": plenty of real files omit the blank separator, and some carry
    /// leading junk. Attribute pairs from an OFX 2.x processing instruction are folded into
    /// the same dictionary so callers need not care which dialect supplied them.
    /// </remarks>
    private static OfxHeader ParseHeader(string headerText, ReadOnlySpan<char> body)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in headerText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            values[line[..colon].Trim().ToUpperInvariant()] = line[(colon + 1)..].Trim();
        }

        bool isXml = false;
        ReadOnlySpan<char> leading = body[..Math.Min(body.Length, 512)];

        if (leading.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            isXml = true;
        }

        int ofxPi = leading.IndexOf("<?OFX", StringComparison.OrdinalIgnoreCase);
        if (ofxPi >= 0)
        {
            isXml = true;

            int close = leading[ofxPi..].IndexOf("?>", StringComparison.Ordinal);
            if (close > 0)
            {
                ReadAttributes(leading.Slice(ofxPi, close).ToString(), values);
            }
        }

        int major = isXml ? 2 : 1;

        if (values.TryGetValue("OFXHEADER", out string? declared)
            && int.TryParse(declared, out int headerVersion))
        {
            // OFXHEADER:100 is SGML, 200 is XML. The marker tags above are more reliable, so
            // this only fills in when neither was present.
            major = headerVersion >= 200 ? 2 : (isXml ? 2 : 1);
        }

        return new OfxHeader(major, values);
    }

    private static void ReadAttributes(string text, Dictionary<string, string> into)
    {
        int index = 0;

        while (index < text.Length)
        {
            int equals = text.IndexOf('=', index);
            if (equals < 0 || equals + 1 >= text.Length)
            {
                return;
            }

            int nameStart = equals;
            while (nameStart > index && !char.IsWhiteSpace(text[nameStart - 1]))
            {
                nameStart--;
            }

            char quote = text[equals + 1];
            if (quote is not ('"' or '\''))
            {
                index = equals + 1;
                continue;
            }

            int end = text.IndexOf(quote, equals + 2);
            if (end < 0)
            {
                return;
            }

            string name = text[nameStart..equals].Trim().ToUpperInvariant();
            if (name.Length > 0)
            {
                into[name] = text[(equals + 2)..end];
            }

            index = end + 1;
        }
    }

    private static OfxNode BuildTree(ReadOnlySpan<char> body, List<ImportDiagnostic> diagnostics)
    {
        var root = new OfxNode("#document");
        var stack = new Stack<OfxNode>();
        stack.Push(root);

        int position = 0;

        while (position < body.Length)
        {
            int open = body[position..].IndexOf('<');
            if (open < 0)
            {
                break;
            }

            open += position;

            // Text preceding this tag belongs to whatever leaf is currently open.
            if (open > position)
            {
                ReadOnlySpan<char> text = body[position..open];
                if (!text.IsWhiteSpace())
                {
                    OfxNode current = stack.Peek();
                    string decoded = Collapse(OfxValueParser.DecodeEntities(text.ToString()));

                    if (current.IsLeaf && current != root)
                    {
                        current.Value = current.Value is null ? decoded : current.Value + " " + decoded;
                    }
                }
            }

            int close = body[open..].IndexOf('>');
            if (close < 0)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportSeverity.Warning,
                    OfxDiagnostic.TruncatedFile,
                    "The file ends in the middle of a tag; everything up to that point was read."));
                break;
            }

            close += open;
            ReadOnlySpan<char> inner = body[(open + 1)..close];
            position = close + 1;

            if (inner.Length == 0)
            {
                continue;
            }

            // Processing instructions and comments carry no statement data.
            if (inner[0] is '?' or '!')
            {
                if (inner.StartsWith("!--", StringComparison.Ordinal)
                    && !inner.EndsWith("--", StringComparison.Ordinal))
                {
                    int commentEnd = body[position..].IndexOf("-->", StringComparison.Ordinal);
                    position = commentEnd < 0 ? body.Length : position + commentEnd + 3;
                }

                continue;
            }

            if (inner[0] == '/')
            {
                CloseTag(inner[1..].Trim().ToString(), stack, root, diagnostics);
                continue;
            }

            bool selfClosing = inner[^1] == '/';
            ReadOnlySpan<char> nameSpan = selfClosing ? inner[..^1] : inner;

            // Attributes are not used by any OFX element we read, but they exist on a few
            // vendor extensions and must not become part of the tag name.
            int space = nameSpan.IndexOfAny(' ', '\t', '\r');
            if (space > 0)
            {
                nameSpan = nameSpan[..space];
            }

            string name = nameSpan.Trim().ToString().ToUpperInvariant();
            if (name.Length == 0)
            {
                continue;
            }

            // The rule that makes OFX 1.x readable at all. A tag that has taken text is a
            // leaf, and SGML lets its closing tag be omitted — so the arrival of any new tag
            // ends it. Without this, every field in a transaction would nest inside the field
            // before it instead of sitting alongside it.
            while (stack.Count > 1 && stack.Peek().Value is not null)
            {
                stack.Pop();
            }

            var node = new OfxNode(name);
            stack.Peek().Add(node);

            if (!selfClosing)
            {
                stack.Push(node);
            }
        }

        if (stack.Count > 1)
        {
            // Unbalanced or truncated. Everything read so far is still usable, so close the
            // open elements silently rather than discarding the statement.
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Info,
                OfxDiagnostic.UnclosedTag,
                $"{stack.Count - 1} tag(s) were left open at the end of the file and were closed automatically."));
        }

        OfxNode? ofx = root.Child(RootTag) ?? root.Descendants(RootTag).FirstOrDefault();

        if (ofx is null)
        {
            throw new OfxParseException(
                "No <OFX> element was found, so this is not an OFX statement file.");
        }

        return ofx;
    }

    /// <summary>
    /// Handles a closing tag, tolerating the two ways real files get this wrong.
    /// </summary>
    /// <remarks>
    /// In SGML the leaves beneath an aggregate are usually left unclosed, so a closing tag
    /// legitimately unwinds several levels. A closing tag naming something that was never
    /// opened is simply corrupt, and is dropped rather than allowed to unwind the document.
    /// </remarks>
    private static void CloseTag(
        string name,
        Stack<OfxNode> stack,
        OfxNode root,
        List<ImportDiagnostic> diagnostics)
    {
        if (name.Length == 0)
        {
            return;
        }

        bool present = stack.Any(n =>
            n != root && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!present)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                OfxDiagnostic.StrayEndTag,
                $"Ignored a stray </{name}> that closes nothing."));
            return;
        }

        while (stack.Count > 1)
        {
            OfxNode popped = stack.Pop();

            if (string.Equals(popped.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Anything popped on the way to the target was never closed explicitly, so it was
            // a leaf, not an aggregate. If it collected children it was an empty leaf that
            // structural inference briefly mistook for a container — for example a bare
            // <MEMO> followed by <TRNAMT>. Lift those children back to where they belong.
            // Document order is preserved: the mistaken parent is its own parent's last
            // child, so appending after it puts everything back in sequence.
            if (popped.Children.Count > 0 && stack.Count > 0)
            {
                stack.Peek().AdoptChildrenFrom(popped);
            }
        }
    }

    /// <summary>Collapses internal whitespace runs, as OFX values are single-line by nature.</summary>
    private static string Collapse(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;

        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
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
