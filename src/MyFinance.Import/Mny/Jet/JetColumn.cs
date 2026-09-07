namespace MyFinance.Import.Mny.Jet;

/// <summary>The Jet column types this reader understands.</summary>
/// <remarks>
/// The numbering is Jet's own. Types absent here (attachments, replication identifiers)
/// do not occur in a Money file and are surfaced as raw bytes rather than guessed at.
/// </remarks>
public enum JetColumnType
{
    Boolean = 0x01,
    Byte = 0x02,
    Int16 = 0x03,
    Int32 = 0x04,

    /// <summary>Currency: a 64-bit integer scaled by 10,000.</summary>
    Currency = 0x05,

    Single = 0x06,
    Double = 0x07,

    /// <summary>A double counting days from 1899-12-30.</summary>
    DateTime = 0x08,

    Binary = 0x09,
    Text = 0x0A,

    /// <summary>Long binary, held inline or on a long-value page.</summary>
    Ole = 0x0B,

    /// <summary>Long text, held inline or on a long-value page.</summary>
    Memo = 0x0C,

    Guid = 0x0F,
    Numeric = 0x10,
}

/// <summary>One column of a Jet table, as described by its 25-byte definition.</summary>
/// <param name="Name">Column name.</param>
/// <param name="Type">Storage type.</param>
/// <param name="Index">Column number, which is also its bit in the row's null mask.</param>
/// <param name="VariableIndex">Slot in the row's variable-length offset table.</param>
/// <param name="FixedOffset">Byte offset into the row's fixed-length area.</param>
/// <param name="Length">Declared length in bytes.</param>
/// <param name="IsFixedLength">Whether the value lives in the fixed area.</param>
public sealed record JetColumn(
    string Name,
    JetColumnType Type,
    int Index,
    int VariableIndex,
    int FixedOffset,
    int Length,
    bool IsFixedLength)
{
    /// <summary>Size of one column definition inside a table definition block.</summary>
    internal const int DefinitionSize = 25;

    // Offsets within that 25-byte definition, confirmed against a real Money file.
    internal const int OffsetType = 0;
    internal const int OffsetNumber = 5;
    internal const int OffsetVariableIndex = 7;
    internal const int OffsetFlags = 15;
    internal const int OffsetFixedOffset = 21;
    internal const int OffsetLength = 23;

    /// <summary>Set when the column's value sits in the fixed-length part of the row.</summary>
    internal const byte FlagFixedLength = 0x01;
}
