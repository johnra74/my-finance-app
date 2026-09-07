using System.Buffers.Binary;

namespace MyFinance.Import.Mny.Jet;

/// <summary>
/// One table: its columns, taken from a table-definition page, and its rows.
/// </summary>
/// <remarks>
/// <para>
/// Jet does not keep the table's name here — names live in the system catalog, which a
/// Money file encrypts. That is deliberate on Money's part and not worth fighting: a table
/// is identified instead by the columns it declares, which are in the clear. See
/// <see cref="MoneyTables" />.
/// </para>
/// </remarks>
public sealed class JetTable
{
    // Offsets inside a table-definition block, confirmed against a real Money file.
    private const int OffsetDefinitionLength = 0x08;
    private const int OffsetRowCount = 0x10;
    private const int OffsetTableType = 0x28;
    private const int OffsetVariableColumns = 0x2B;
    private const int OffsetColumnCount = 0x2D;
    private const int OffsetRealIndexCount = 0x33;
    private const int OffsetIndexBlock = 0x3F;
    private const int RealIndexEntrySize = 12;

    private readonly JetDatabase _database;
    private readonly Dictionary<string, int> _ordinals;

    private JetTable(
        JetDatabase database,
        int definitionPage,
        IReadOnlyList<JetColumn> columns,
        int declaredRowCount)
    {
        _database = database;
        DefinitionPage = definitionPage;
        Columns = columns;
        DeclaredRowCount = declaredRowCount;

        _ordinals = new Dictionary<string, int>(columns.Count, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < columns.Count; i++)
        {
            // A duplicate name would be a corrupt definition; keep the first and carry on.
            _ordinals.TryAdd(columns[i].Name, i);
        }
    }

    /// <summary>The page its definition starts on, which is also its identity.</summary>
    public int DefinitionPage { get; }

    public IReadOnlyList<JetColumn> Columns { get; }

    /// <summary>The row count the definition claims, before any row is read.</summary>
    public int DeclaredRowCount { get; }

    public bool Has(string column) => _ordinals.ContainsKey(column);

    internal bool TryGetOrdinal(string column, out int ordinal) =>
        _ordinals.TryGetValue(column, out ordinal);

    /// <summary>Reads a table definition, or returns null if the page is not one.</summary>
    internal static JetTable? TryRead(JetDatabase database, int page)
    {
        try
        {
            byte[] definition = ReadDefinition(database, page);

            if (definition.Length < OffsetIndexBlock)
            {
                return null;
            }

            int columnCount = BinaryPrimitives.ReadUInt16LittleEndian(
                definition.AsSpan(OffsetColumnCount));
            int realIndexes = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                definition.AsSpan(OffsetRealIndexCount));

            if (columnCount is 0 or > 4096 || realIndexes is < 0 or > 4096)
            {
                return null;
            }

            int offset = OffsetIndexBlock + (realIndexes * RealIndexEntrySize);
            var columns = new List<JetColumn>(columnCount);

            // Definitions first, then the names, in the same order.
            var pending = new List<(JetColumnType Type, int Index, int VarIndex, int FixedOffset, int Length, bool Fixed)>(columnCount);

            for (int i = 0; i < columnCount; i++)
            {
                if (offset + JetColumn.DefinitionSize > definition.Length)
                {
                    return null;
                }

                ReadOnlySpan<byte> d = definition.AsSpan(offset, JetColumn.DefinitionSize);

                pending.Add((
                    (JetColumnType)d[JetColumn.OffsetType],
                    BinaryPrimitives.ReadUInt16LittleEndian(d[JetColumn.OffsetNumber..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(d[JetColumn.OffsetVariableIndex..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(d[JetColumn.OffsetFixedOffset..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(d[JetColumn.OffsetLength..]),
                    (d[JetColumn.OffsetFlags] & JetColumn.FlagFixedLength) != 0));

                offset += JetColumn.DefinitionSize;
            }

            foreach ((JetColumnType type, int index, int varIndex, int fixedOffset, int length, bool isFixed) in pending)
            {
                if (offset + 2 > definition.Length)
                {
                    return null;
                }

                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(definition.AsSpan(offset));
                offset += 2;

                if (nameLength < 0 || offset + nameLength > definition.Length)
                {
                    return null;
                }

                string name = System.Text.Encoding.Unicode.GetString(definition, offset, nameLength);
                offset += nameLength;

                if (name.Length == 0)
                {
                    return null;
                }

                columns.Add(new JetColumn(name, type, index, varIndex, fixedOffset, length, isFixed));
            }

            int rowCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                definition.AsSpan(OffsetRowCount));

            return new JetTable(database, page, columns, Math.Max(rowCount, 0));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Joins a definition that spans several pages into one buffer.</summary>
    private static byte[] ReadDefinition(JetDatabase database, int page)
    {
        ReadOnlySpan<byte> first = database.Page(page);
        int declared = (int)BinaryPrimitives.ReadUInt32LittleEndian(first[OffsetDefinitionLength..]);

        var buffer = new List<byte>(Math.Max(declared + OffsetIndexBlock, database.PageSize));
        buffer.AddRange(first);

        int next = (int)BinaryPrimitives.ReadUInt32LittleEndian(first[JetDatabase.OffsetNextPage..]);
        int guard = 0;

        while (next > 0 && next < database.PageCount && guard++ < 64)
        {
            ReadOnlySpan<byte> continuation = database.Page(next);

            if (continuation[0] != JetDatabase.PageTypeTableDefinition)
            {
                break;
            }

            // A continuation repeats the 8-byte page header before resuming the block.
            buffer.AddRange(continuation[8..]);
            next = (int)BinaryPrimitives.ReadUInt32LittleEndian(continuation[JetDatabase.OffsetNextPage..]);
        }

        return [.. buffer];
    }

    /// <summary>Reads every live row of the table.</summary>
    public IEnumerable<JetRow> Rows()
    {
        foreach (int page in _database.DataPagesFor(DefinitionPage))
        {
            byte[] buffer = _database.PageCopy(page);
            int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(
                buffer.AsSpan(JetDatabase.OffsetDataRowCount));

            for (int r = 0; r < rowCount; r++)
            {
                int slot = JetDatabase.OffsetDataRowOffsets + (r * 2);

                if (slot + 2 > buffer.Length)
                {
                    break;
                }

                int raw = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(slot));

                // Deleted rows and pointers to an overflow page carry flags in the top bits.
                if ((raw & 0xC000) != 0)
                {
                    continue;
                }

                int start = raw & 0x1FFF;
                int end = r == 0
                    ? buffer.Length
                    : BinaryPrimitives.ReadUInt16LittleEndian(
                        buffer.AsSpan(JetDatabase.OffsetDataRowOffsets + ((r - 1) * 2))) & 0x1FFF;

                if (start >= end || end > buffer.Length)
                {
                    continue;
                }

                JetRow? row = TryDecode(buffer.AsSpan(start, end - start));

                if (row is not null)
                {
                    yield return row;
                }
            }
        }
    }

    /// <summary>
    /// Decodes one row.
    /// </summary>
    /// <remarks>
    /// The layout, reading a row from both ends: a 2-byte column count, then the
    /// fixed-length values; and from the back, the null mask, a 2-byte count of
    /// variable-length columns, and one more offset than there are such columns — the extra
    /// one marking where their data ends.
    /// </remarks>
    private JetRow? TryDecode(ReadOnlySpan<byte> row)
    {
        if (row.Length < 6)
        {
            return null;
        }

        int columnsInRow = BinaryPrimitives.ReadUInt16LittleEndian(row);

        if (columnsInRow is <= 0 or > 4096)
        {
            return null;
        }

        int maskSize = (columnsInRow + 7) / 8;

        if (row.Length < maskSize + 4)
        {
            return null;
        }

        ReadOnlySpan<byte> nullMask = row[^maskSize..];
        int cursor = row.Length - maskSize;

        int variableCount = BinaryPrimitives.ReadUInt16LittleEndian(row[(cursor - 2)..]);
        cursor -= 2;

        if (variableCount < 0 || variableCount > columnsInRow)
        {
            return null;
        }

        if (cursor - (2 * (variableCount + 1)) < 0)
        {
            return null;
        }

        // Stored in reverse, so entry k sits k slots back from the count.
        Span<int> variableOffsets = variableCount + 1 <= 128
            ? stackalloc int[variableCount + 1]
            : new int[variableCount + 1];

        for (int k = 0; k <= variableCount; k++)
        {
            variableOffsets[k] = BinaryPrimitives.ReadUInt16LittleEndian(row[(cursor - (2 * (k + 1)))..]);
        }

        var values = new object?[Columns.Count];

        for (int i = 0; i < Columns.Count; i++)
        {
            JetColumn column = Columns[i];

            if (column.Index >= columnsInRow)
            {
                continue;
            }

            // Jet stores a boolean as the mask bit itself: set means true, never null.
            bool present = (nullMask[column.Index / 8] >> (column.Index % 8) & 1) == 1;

            if (column.Type == JetColumnType.Boolean)
            {
                values[i] = present;
                continue;
            }

            if (!present)
            {
                continue;
            }

            ReadOnlySpan<byte> raw;

            if (column.IsFixedLength)
            {
                int start = 2 + column.FixedOffset;
                int length = Math.Max(column.Length, 8);

                if (start < 0 || start >= row.Length)
                {
                    continue;
                }

                raw = row[start..Math.Min(start + length, row.Length)];
            }
            else
            {
                if (column.VariableIndex + 1 >= variableOffsets.Length)
                {
                    continue;
                }

                int start = variableOffsets[column.VariableIndex];
                int end = variableOffsets[column.VariableIndex + 1];

                if (start < 0 || end < start || end > row.Length)
                {
                    continue;
                }

                raw = row[start..end];
            }

            values[i] = Decode(column, raw);
        }

        return new JetRow(this, values);
    }

    private object? Decode(JetColumn column, ReadOnlySpan<byte> raw)
    {
        if (raw.Length == 0)
        {
            return null;
        }

        try
        {
            switch (column.Type)
            {
                case JetColumnType.Byte:
                    return (int)raw[0];
                case JetColumnType.Int16:
                    return (int)BinaryPrimitives.ReadInt16LittleEndian(raw);
                case JetColumnType.Int32:
                    return BinaryPrimitives.ReadInt32LittleEndian(raw);
                case JetColumnType.Currency:
                    return JetValues.ReadCurrencyUnits(raw);
                case JetColumnType.Single:
                    return (double)BitConverter.ToSingle(raw);
                case JetColumnType.Double:
                    return BitConverter.ToDouble(raw);
                case JetColumnType.DateTime:
                    return JetValues.TryReadDateTime(raw, out DateTime when) ? when : null;
                case JetColumnType.Text:
                    return JetValues.ReadText(raw);
                case JetColumnType.Memo:
                    return JetValues.ReadText(ReadLongValue(raw));
                case JetColumnType.Ole:
                case JetColumnType.Binary:
                case JetColumnType.Guid:
                case JetColumnType.Numeric:
                default:
                    return raw.ToArray();
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a long value, which is either inline or out on its own pages.
    /// </summary>
    /// <remarks>
    /// The 12-byte header carries the length in its low three bytes and the storage choice
    /// in the top byte. Reading it as text without stripping the header is what turns an
    /// English memo into Chinese, because the header bytes pair up into CJK code points.
    /// </remarks>
    private byte[] ReadLongValue(ReadOnlySpan<byte> raw)
    {
        const int headerSize = 12;
        const byte inlineFlag = 0x80;
        const byte singlePageFlag = 0x40;

        if (raw.Length < headerSize)
        {
            return raw.ToArray();
        }

        uint header = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        int length = (int)(header & 0x00FFFFFF);
        byte flags = (byte)(header >> 24);

        if ((flags & inlineFlag) != 0)
        {
            int available = Math.Min(length, raw.Length - headerSize);
            return available <= 0 ? [] : raw.Slice(headerSize, available).ToArray();
        }

        uint pointer = BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]);
        return _database.ReadLongValuePages(
            (int)(pointer >> 8),
            (int)(pointer & 0xFF),
            length,
            (flags & singlePageFlag) != 0);
    }
}
