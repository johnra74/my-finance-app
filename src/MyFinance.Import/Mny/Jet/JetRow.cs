using System.Globalization;

namespace MyFinance.Import.Mny.Jet;

/// <summary>One row, as a set of decoded column values.</summary>
public sealed class JetRow
{
    private readonly JetTable _table;
    private readonly object?[] _values;

    internal JetRow(JetTable table, object?[] values)
    {
        _table = table;
        _values = values;
    }

    public object? this[string column] =>
        _table.TryGetOrdinal(column, out int ordinal) ? _values[ordinal] : null;

    public object? this[int ordinal] => _values[ordinal];

    public bool Has(string column) => this[column] is not null;

    public int? Int(string column) => this[column] switch
    {
        int i => i,
        short s => s,
        byte b => b,
        long l => (int)l,
        _ => null,
    };

    public long? Long(string column) => this[column] switch
    {
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        _ => null,
    };

    public double? Double(string column) => this[column] switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        _ => null,
    };

    public bool Bool(string column) => this[column] is true;

    public DateTime? Date(string column) => this[column] as DateTime?;

    public string? Text(string column)
    {
        string? value = this[column] as string;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// A currency column in its raw 1/10,000 units.
    /// </summary>
    /// <remarks>
    /// Returned unscaled so the caller decides how to reach cents. Dividing here would round
    /// twice — once to cents and again when the caller totals — and a migration that loses a
    /// penny per row loses real money across twenty thousand of them.
    /// </remarks>
    public long? CurrencyUnits(string column) => Long(column);

    public override string ToString() => string.Join(
        ", ",
        _table.Columns.Select((c, i) => $"{c.Name}={JetValues.Describe(_values[i])}"));

    /// <summary>Renders the row for a diagnostic, with the culture's own formatting.</summary>
    public string ToString(CultureInfo culture) => string.Join(
        ", ",
        _table.Columns.Select((c, i) => $"{c.Name}={Convert.ToString(_values[i], culture)}"));
}
