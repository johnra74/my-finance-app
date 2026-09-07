using MyFinance.Core.Enums;
using MyFinance.Import.Model;
using MyFinance.Import.Ofx;
using MyFinance.Import.Qif;

namespace MyFinance.Import;

/// <summary>
/// Reads a downloaded statement, working out for itself which format it is in.
/// </summary>
/// <remarks>
/// The extension is a hint, not the answer: files get renamed, and a bank that labels its
/// export <c>.qfx</c> occasionally writes plain QIF into it. The content decides.
/// </remarks>
public static class StatementFileReader
{
    /// <summary>Extensions the file picker offers.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [".ofx", ".qfx", ".qbo", ".qif"];

    public static ImportedFile Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return DetectFormat(path) == ImportFormat.Qif
            ? QifParser.ParseFile(path)
            : OfxStatementMapper.ToImported(OfxStatementReader.ReadFile(path));
    }

    /// <summary>
    /// Works out which format a file is in by looking at what it starts with.
    /// </summary>
    /// <remarks>
    /// OFX in either dialect always reaches an <c>&lt;OFX&gt;</c> tag; QIF always opens with
    /// a <c>!</c> directive. Checking for markup first is the safer order, because a QIF file
    /// can never contain angle brackets in that position while an OFX header line can start
    /// with almost anything.
    /// </remarks>
    public static ImportFormat DetectFormat(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var reader = new StreamReader(path);

        for (int line = 0; line < 40; line++)
        {
            string? text = reader.ReadLine()?.Trim();

            if (text is null)
            {
                break;
            }

            if (text.Length == 0)
            {
                continue;
            }

            if (text.StartsWith('<'))
            {
                return ImportFormat.Ofx;
            }

            if (text.StartsWith('!'))
            {
                return ImportFormat.Qif;
            }
        }

        // Neither marker turned up in the opening lines. OFX is the likelier of the two and
        // its reader gives the better diagnostic when it is wrong.
        return ImportFormat.Ofx;
    }
}
