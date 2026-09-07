namespace MyFinance.Import.Ofx;

/// <summary>Codes the OFX reader attaches to its diagnostics.</summary>
public static class OfxDiagnostic
{
    public const string StrayEndTag = "ofx.stray_end_tag";
    public const string UnclosedTag = "ofx.unclosed_tag";
    public const string TruncatedFile = "ofx.truncated";
    public const string UnreadableAmount = "ofx.bad_amount";
    public const string RoundedAmount = "ofx.rounded_amount";
    public const string UnreadableDate = "ofx.bad_date";
    public const string MissingDate = "ofx.missing_date";
    public const string BankStatus = "ofx.bank_status";
    public const string InvestmentUnsupported = "ofx.investment_unsupported";
    public const string NoStatements = "ofx.no_statements";
    public const string CorrectedTransaction = "ofx.corrected_transaction";
}
