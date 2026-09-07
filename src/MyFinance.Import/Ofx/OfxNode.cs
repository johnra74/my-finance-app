using System.Diagnostics;
using System.Globalization;
using MyFinance.Core.Primitives;

namespace MyFinance.Import.Ofx;

/// <summary>
/// One element of a parsed OFX document: either an aggregate with children, or a leaf
/// carrying a value.
/// </summary>
/// <remarks>
/// Deliberately untyped. OFX is extended freely by every vendor, and a tree the reader can
/// query loosely copes with tags we have never seen, whereas a fixed object model refuses
/// to load the moment a bank adds one.
/// </remarks>
[DebuggerDisplay("{Name,nq} = {Value}")]
public sealed class OfxNode
{
    private readonly List<OfxNode> _children = [];

    public OfxNode(string name, string? value = null)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Tag name, upper-cased — OFX tags are case-insensitive in practice.</summary>
    public string Name { get; }

    /// <summary>Text content for a leaf; null for an aggregate.</summary>
    public string? Value { get; internal set; }

    public IReadOnlyList<OfxNode> Children => _children;

    public bool IsLeaf => _children.Count == 0;

    internal void Add(OfxNode child) => _children.Add(child);

    /// <summary>
    /// Moves another node's children onto this one, in order, and empties it.
    /// </summary>
    /// <remarks>
    /// Repairs the one case structural inference cannot get right on its own: an empty leaf
    /// whose closing tag was omitted, which briefly looks like an aggregate and collects the
    /// fields that should have been its siblings. See <c>OfxParser.CloseTag</c>.
    /// </remarks>
    internal void AdoptChildrenFrom(OfxNode other)
    {
        _children.AddRange(other._children);
        other._children.Clear();
    }

    /// <summary>First direct child with this name, or null.</summary>
    public OfxNode? Child(string name) =>
        _children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>All direct children with this name.</summary>
    public IEnumerable<OfxNode> ChildrenNamed(string name) =>
        _children.Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every node anywhere beneath this one with the given name, depth-first.
    /// </summary>
    /// <remarks>
    /// Statements are located this way rather than by an exact path, because the wrapping
    /// aggregates differ between banks and between OFX versions while the statement tag
    /// itself never does.
    /// </remarks>
    public IEnumerable<OfxNode> Descendants(string name)
    {
        foreach (OfxNode child in _children)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }

            foreach (OfxNode nested in child.Descendants(name))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Follows a slash-separated path of child names, e.g. "STMTRS/BANKACCTFROM".</summary>
    public OfxNode? Path(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        OfxNode? current = this;

        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current?.Child(part);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>Text of a named child, trimmed, or null when absent or empty.</summary>
    public string? Text(string name)
    {
        string? value = Child(name)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Named child parsed as an OFX amount, or null when absent or unreadable.</summary>
    public Money? Amount(string name) =>
        OfxValueParser.TryParseAmount(Text(name), out Money amount, out _) ? amount : null;

    /// <summary>Named child parsed as an OFX timestamp, or null.</summary>
    public OfxTimestamp? Timestamp(string name) =>
        OfxValueParser.TryParseTimestamp(Text(name), out OfxTimestamp stamp) ? stamp : null;

    /// <summary>Named child parsed as an integer, or null.</summary>
    public int? Integer(string name) =>
        int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;

    public override string ToString() => IsLeaf ? $"{Name}={Value}" : $"{Name} ({_children.Count})";
}
