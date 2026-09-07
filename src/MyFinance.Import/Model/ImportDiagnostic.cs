namespace MyFinance.Import.Model;

/// <summary>How much a reading observation matters.</summary>
public enum ImportSeverity
{
    /// <summary>Worth recording, nothing to act on.</summary>
    Info = 0,

    /// <summary>The file is irregular but usable; some data may be missing.</summary>
    Warning = 1,

    /// <summary>Nothing can be imported from this file.</summary>
    Error = 2,
}

/// <summary>
/// One observation made while reading a downloaded file.
/// </summary>
/// <remarks>
/// Reading a bank file collects problems rather than throwing on the first one. A statement
/// with one malformed row out of four hundred should import the other three hundred and
/// ninety-nine and say what it skipped — refusing the file outright helps nobody.
/// </remarks>
/// <param name="Severity">How much it matters.</param>
/// <param name="Code">Stable identifier, for tests and for grouping in the UI.</param>
/// <param name="Message">Human-readable explanation, shown to the user as-is.</param>
public sealed record ImportDiagnostic(ImportSeverity Severity, string Code, string Message)
{
    public override string ToString() => $"{Severity}: {Message}";
}
