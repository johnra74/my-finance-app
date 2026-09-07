using System.Buffers.Binary;
using System.Text;

namespace MyFinance.Import.Mny.Jet;

/// <summary>
/// A read-only reader for a Jet 4 database, which is what a Microsoft Money file is.
/// </summary>
/// <remarks>
/// <para>
/// Money writes the "MSISAM" flavour of Jet 4: 4 KB pages, and the system catalog on
/// pages 1 to 14 encrypted even when no password is set. Those pages hold nothing but the
/// object names, so this reader ignores them entirely and identifies tables by the columns
/// they declare, which Money leaves in the clear. That removes any need to reproduce
/// Money's key derivation, and with it a whole class of silently-wrong results.
/// </para>
/// <para>
/// Nothing here writes. A migration reads the user's book once; the original file is never
/// opened for writing, so a failed migration cannot damage the only copy of twenty-five
/// years of records.
/// </para>
/// </remarks>
public sealed class JetDatabase
{
    internal const int PageTypeDatabaseDefinition = 0x00;
    internal const int PageTypeData = 0x01;
    internal const int PageTypeTableDefinition = 0x02;

    internal const int OffsetNextPage = 0x04;
    internal const int OffsetDataOwner = 0x04;
    internal const int OffsetDataRowCount = 0x0C;
    internal const int OffsetDataRowOffsets = 0x0E;

    private const int Jet4PageSize = 4096;
    private const byte Jet4VersionByte = 0x01;

    /// <summary>
    /// Refuses a file large enough to be a problem to hold in memory.
    /// </summary>
    /// <remarks>
    /// The whole file is read at once because a migration walks nearly all of it and jumps
    /// about for long values. A real Money book runs to tens of megabytes; anything past
    /// this is not one, and reading it would be the wrong kind of surprise.
    /// </remarks>
    public const long MaximumFileSize = 512L * 1024 * 1024;

    private readonly byte[] _bytes;
    private readonly Dictionary<int, List<int>> _dataPagesByOwner;
    private readonly Lazy<IReadOnlyList<JetTable>> _tables;

    private JetDatabase(byte[] bytes)
    {
        _bytes = bytes;
        PageCount = bytes.Length / Jet4PageSize;
        _dataPagesByOwner = IndexDataPages();
        _tables = new Lazy<IReadOnlyList<JetTable>>(ReadTables);
    }

    public int PageSize => Jet4PageSize;

    public int PageCount { get; }

    /// <summary>Every table definition found in the file.</summary>
    public IReadOnlyList<JetTable> Tables => _tables.Value;

    public static JetDatabase Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var info = new FileInfo(path);

        if (!info.Exists)
        {
            throw new JetException($"There is no file at {path}.");
        }

        if (info.Length > MaximumFileSize)
        {
            throw new JetException(
                $"{info.Name} is {info.Length / (1024 * 1024)} MB, which is far larger than any Money file. Check that this is the right file.");
        }

        byte[] bytes;

        try
        {
            // Opened for reading only, and shared, so a copy that is open elsewhere still reads.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (IOException ex)
        {
            throw new JetException($"{info.Name} could not be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new JetException($"{info.Name} could not be read: {ex.Message}", ex);
        }

        return Open(bytes, info.Name);
    }

    public static JetDatabase Open(byte[] bytes, string name = "the file")
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length < Jet4PageSize)
        {
            throw new JetException($"{name} is too short to be a Money file.");
        }

        if (bytes[0] != 0x00 || bytes[1] != 0x01)
        {
            throw new JetException($"{name} is not a Microsoft Money or Access database.");
        }

        string engine = Encoding.ASCII.GetString(bytes, 4, 15).TrimEnd('\0', ' ');

        if (bytes[0x14] != Jet4VersionByte)
        {
            throw new JetException(
                $"{name} is an older database format ('{engine}', version {bytes[0x14]}). Only the format Money 2002 and later writes can be read.");
        }

        var database = new JetDatabase(bytes);

        // Pages 1 to 14 are encrypted in every Money file. If the user data is encrypted
        // too, no definition parses, and saying so plainly beats returning an empty book.
        if (database.Tables.Count == 0)
        {
            throw new JetException(
                $"No tables could be read from {name}. If the file is password-protected in Money, remove the password there first and save a copy.");
        }

        return database;
    }

    internal ReadOnlySpan<byte> Page(int page)
    {
        if (page < 0 || page >= PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "That page is not in the file.");
        }

        return _bytes.AsSpan(page * Jet4PageSize, Jet4PageSize);
    }

    internal byte[] PageCopy(int page) => Page(page).ToArray();

    internal IReadOnlyList<int> DataPagesFor(int definitionPage) =>
        _dataPagesByOwner.TryGetValue(definitionPage, out List<int>? pages) ? pages : [];

    /// <summary>Gathers a long value from the chain of pages holding it.</summary>
    internal byte[] ReadLongValuePages(int page, int rowNumber, int length, bool singlePage)
    {
        if (length <= 0)
        {
            return [];
        }

        var collected = new List<byte>(length);
        int guard = 0;

        while (page > 0 && page < PageCount && collected.Count < length && guard++ < 4096)
        {
            ReadOnlySpan<byte> buffer = Page(page);

            if (buffer[0] != PageTypeData)
            {
                break;
            }

            int rows = BinaryPrimitives.ReadUInt16LittleEndian(buffer[OffsetDataRowCount..]);

            if (rowNumber < 0 || rowNumber >= rows)
            {
                break;
            }

            int start = BinaryPrimitives.ReadUInt16LittleEndian(
                buffer[(OffsetDataRowOffsets + (rowNumber * 2))..]) & 0x1FFF;
            int end = rowNumber == 0
                ? Jet4PageSize
                : BinaryPrimitives.ReadUInt16LittleEndian(
                    buffer[(OffsetDataRowOffsets + ((rowNumber - 1) * 2))..]) & 0x1FFF;

            if (start >= end || end > Jet4PageSize)
            {
                break;
            }

            ReadOnlySpan<byte> chunk = buffer[start..end];

            if (singlePage)
            {
                collected.AddRange(chunk);
                break;
            }

            if (chunk.Length < 4)
            {
                break;
            }

            uint next = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
            collected.AddRange(chunk[4..]);

            page = (int)(next >> 8);
            rowNumber = (int)(next & 0xFF);
        }

        return collected.Count <= length ? [.. collected] : [.. collected.Take(length)];
    }

    private Dictionary<int, List<int>> IndexDataPages()
    {
        var index = new Dictionary<int, List<int>>();

        for (int page = 0; page < PageCount; page++)
        {
            int offset = page * Jet4PageSize;

            if (_bytes[offset] != PageTypeData || _bytes[offset + 1] != 0x01)
            {
                continue;
            }

            int owner = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                _bytes.AsSpan(offset + OffsetDataOwner));

            if (owner <= 0 || owner >= PageCount)
            {
                continue;
            }

            if (!index.TryGetValue(owner, out List<int>? pages))
            {
                pages = [];
                index[owner] = pages;
            }

            pages.Add(page);
        }

        return index;
    }

    private IReadOnlyList<JetTable> ReadTables()
    {
        var tables = new List<JetTable>();

        for (int page = 0; page < PageCount; page++)
        {
            int offset = page * Jet4PageSize;

            if (_bytes[offset] != PageTypeTableDefinition || _bytes[offset + 1] != 0x01)
            {
                continue;
            }

            JetTable? table = JetTable.TryRead(this, page);

            if (table is not null)
            {
                tables.Add(table);
            }
        }

        return tables;
    }
}
