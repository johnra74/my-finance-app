using MyFinance.Core.Primitives;
using MyFinance.Import.Model;
using MyFinance.Import.Ofx.Model;

namespace MyFinance.Import.Ofx;

/// <summary>
/// Turns a parsed OFX tree into statements.
/// </summary>
/// <remarks>
/// Statements are located by searching the tree for <c>STMTRS</c> and <c>CCSTMTRS</c>
/// wherever they appear, rather than by walking a fixed path. The wrapping aggregates differ
/// between banks and between OFX versions; the statement tags themselves never do.
/// </remarks>
public static class OfxStatementReader
{
    private const string BankStatement = "STMTRS";
    private const string CreditCardStatement = "CCSTMTRS";
    private const string InvestmentStatement = "INVSTMTRS";

    public static OfxParseResult Read(OfxDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<ImportDiagnostic>(document.Diagnostics);
        var statements = new List<OfxStatement>();

        string? institution = document.Root.Path("SIGNONMSGSRSV1/SONRS/FI")?.Text("ORG")
            ?? document.Root.Descendants("ORG").FirstOrDefault()?.Value;

        ReadStatus(document.Root.Path("SIGNONMSGSRSV1/SONRS"), "sign-on", diagnostics);

        foreach (OfxNode wrapper in document.Root.Descendants("STMTTRNRS")
            .Concat(document.Root.Descendants("CCSTMTTRNRS"))
            .Concat(document.Root.Descendants("INVSTMTTRNRS")))
        {
            ReadStatus(wrapper, "statement", diagnostics);
        }

        // A bank that rejects the request still returns a perfectly well-formed file with no
        // transactions in it. Without surfacing the status the user is told "0 transactions"
        // and reasonably concludes this application is broken.
        if (diagnostics.Any(d => d.Severity == ImportSeverity.Error))
        {
            return new OfxParseResult
            {
                Header = document.Header,
                Statements = [],
                Diagnostics = diagnostics,
            };
        }

        foreach (OfxNode node in document.Root.Descendants(BankStatement))
        {
            statements.Add(ReadStatement(node, OfxAccountKind.Bank, institution, diagnostics));
        }

        foreach (OfxNode node in document.Root.Descendants(CreditCardStatement))
        {
            statements.Add(ReadStatement(node, OfxAccountKind.CreditCard, institution, diagnostics));
        }

        // Investment statements are recognised and refused rather than partly read. Pulling
        // out the cash legs while dropping every buy and sell would produce a register that
        // looks complete and is wrong, and nothing in this schema can represent a holding.
        foreach (OfxNode node in document.Root.Descendants(InvestmentStatement))
        {
            int cash = node.Descendants("INVBANKTRAN").Count();
            int trades = node.Descendants("INVTRANLIST").Sum(l => l.Children.Count) - cash;

            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                OfxDiagnostic.InvestmentUnsupported,
                $"This is an investment statement, which MyFinance cannot import yet. It was skipped ({cash} cash and {Math.Max(trades, 0)} securities transactions)."));
        }

        if (statements.Count == 0 && !diagnostics.Any(d => d.Severity == ImportSeverity.Error))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Error,
                OfxDiagnostic.NoStatements,
                "This file contains no bank or credit card statement."));
        }

        return new OfxParseResult
        {
            Header = document.Header,
            Statements = statements,
            Diagnostics = diagnostics,
        };
    }

    public static OfxParseResult ReadFile(string path) => Read(OfxParser.ParseFile(path));

    public static OfxParseResult ReadBytes(byte[] bytes) => Read(OfxParser.Parse(bytes));

    private static OfxStatement ReadStatement(
        OfxNode node,
        OfxAccountKind kind,
        string? institution,
        List<ImportDiagnostic> diagnostics)
    {
        OfxNode? accountNode = node.Child("BANKACCTFROM") ?? node.Child("CCACCTFROM");

        var account = new OfxAccountInfo(
            kind,
            accountNode?.Text("BANKID"),
            accountNode?.Text("ACCTID"),
            accountNode?.Text("ACCTTYPE"));

        OfxNode? list = node.Child("BANKTRANLIST");

        var transactions = new List<OfxTransaction>();

        foreach (OfxNode row in list?.ChildrenNamed("STMTTRN") ?? [])
        {
            if (TryReadTransaction(row, diagnostics) is OfxTransaction transaction)
            {
                transactions.Add(transaction);
            }
        }

        return new OfxStatement
        {
            Account = account,
            CurrencyCode = node.Text("CURDEF"),
            PeriodStart = list is null ? null : ReadDate(list, "DTSTART"),
            PeriodEnd = list is null ? null : ReadDate(list, "DTEND"),
            Transactions = transactions,
            LedgerBalance = ReadBalance(node.Child("LEDGERBAL")),
            AvailableBalance = ReadBalance(node.Child("AVAILBAL")),
            Institution = institution,
        };
    }

    private static OfxTransaction? TryReadTransaction(OfxNode row, List<ImportDiagnostic> diagnostics)
    {
        string? postedText = row.Text("DTPOSTED");

        if (!OfxValueParser.TryParseTimestamp(postedText, out OfxTimestamp posted))
        {
            // A row with no readable date cannot be placed in a register at all, so it is
            // dropped — but never silently.
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                postedText is null ? OfxDiagnostic.MissingDate : OfxDiagnostic.UnreadableDate,
                $"Skipped a transaction with an unreadable posted date ({postedText ?? "none given"})."));
            return null;
        }

        string? amountText = row.Text("TRNAMT");

        if (!OfxValueParser.TryParseAmount(amountText, out Money amount, out bool rounded))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                OfxDiagnostic.UnreadableAmount,
                $"Skipped a transaction dated {posted.Date:d} with an unreadable amount ({amountText ?? "none given"})."));
            return null;
        }

        if (rounded)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Info,
                OfxDiagnostic.RoundedAmount,
                $"An amount dated {posted.Date:d} carried more precision than cents and was rounded to {amount.ToAccountingString()}."));
        }

        string? correctsFitId = row.Text("CORRECTFITID");

        if (correctsFitId is not null)
        {
            // Deliberately reported rather than applied. Silently replacing the earlier row
            // would discard the category and payee the user had already put on it.
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Info,
                OfxDiagnostic.CorrectedTransaction,
                $"The bank says a transaction dated {posted.Date:d} corrects an earlier one. It is imported as a new row; check the original."));
        }

        // The structured PAYEE aggregate is rare but clean when a bank supplies it.
        string? name = row.Path("PAYEE")?.Text("NAME") ?? row.Text("NAME");

        return new OfxTransaction
        {
            FitId = row.Text("FITID"),
            TransactionType = row.Text("TRNTYPE"),
            Posted = posted.Date,
            UserDate = OfxValueParser.TryParseDate(row.Text("DTUSER"), out DateOnly user) ? user : null,
            Amount = amount,
            Name = name,
            Memo = row.Text("MEMO"),
            CheckNumber = row.Text("CHECKNUM"),
            CorrectsFitId = correctsFitId,
            Sic = row.Text("SIC"),
        };
    }

    private static OfxBalance? ReadBalance(OfxNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (!OfxValueParser.TryParseAmount(node.Text("BALAMT"), out Money amount, out _))
        {
            return null;
        }

        return new OfxBalance(amount, ReadDate(node, "DTASOF"));
    }

    private static DateOnly? ReadDate(OfxNode node, string name) =>
        OfxValueParser.TryParseDate(node.Text(name), out DateOnly date) ? date : null;

    /// <summary>
    /// Turns a <c>STATUS</c> aggregate into a diagnostic.
    /// </summary>
    /// <remarks>
    /// The bank's own <c>MESSAGE</c> is carried through verbatim. It is almost always more
    /// useful than anything this application could infer — "your credentials have expired",
    /// "requested date range too large" — and paraphrasing it only loses information.
    /// </remarks>
    private static void ReadStatus(OfxNode? parent, string what, List<ImportDiagnostic> diagnostics)
    {
        OfxNode? status = parent?.Child("STATUS");
        if (status is null)
        {
            return;
        }

        int code = status.Integer("CODE") ?? 0;
        string severity = status.Text("SEVERITY")?.ToUpperInvariant() ?? string.Empty;
        string? message = status.Text("MESSAGE");

        if (code == 0 && severity is "" or "INFO")
        {
            return;
        }

        string detail = message is null
            ? $"The bank returned {what} status {code}."
            : $"The bank returned {what} status {code}: {message}";

        ImportSeverity mapped = severity switch
        {
            "ERROR" => ImportSeverity.Error,
            "WARN" => ImportSeverity.Warning,

            // Code 1 means "the data is up to date", which is normal on a repeat download.
            _ when code == 1 => ImportSeverity.Info,
            _ => ImportSeverity.Error,
        };

        diagnostics.Add(new ImportDiagnostic(mapped, OfxDiagnostic.BankStatus, detail));
    }
}
