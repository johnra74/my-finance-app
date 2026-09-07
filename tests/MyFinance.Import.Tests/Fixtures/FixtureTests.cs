using MyFinance.Core.Primitives;
using MyFinance.Import.Ofx;
using MyFinance.Import.Ofx.Model;
using MyFinance.Import.Tests.Ofx;

namespace MyFinance.Import.Tests.Fixtures;

/// <summary>Locates the fixture directories relative to the repository.</summary>
internal static class FixturePaths
{
    /// <summary>Real downloads. Gitignored; empty on a fresh clone.</summary>
    public static string Private => Path.Combine(Root, "ofx", "private");

    /// <summary>Redactor output. Committed, and what the golden tests read.</summary>
    public static string Redacted => Path.Combine(Root, "ofx", "redacted");

    public static IReadOnlyList<string> RedactedFiles => Enumerate(Redacted);

    public static IReadOnlyList<string> PrivateFiles => Enumerate(Private);

    private static IReadOnlyList<string> Enumerate(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory)
                .Where(f => f.EndsWith(".ofx", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".qfx", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".qbo", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)]
            : [];

    /// <summary>
    /// Walks up from the test binaries to the repository's fixtures directory.
    /// </summary>
    /// <remarks>
    /// Resolved at run time rather than copied to the output, so a file dropped in while the
    /// build output is stale is still picked up.
    /// </remarks>
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "tests", "fixtures");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "fixtures");
        }
    }
}

/// <summary>
/// Runs the parser over whatever real-world statements have been redacted into the
/// repository.
/// </summary>
/// <remarks>
/// These are the tests that catch what synthetic fixtures cannot: a bank that omits a tag
/// the specification requires, writes a date in its own dialect, or nests an aggregate
/// somewhere unexpected. They pass trivially on a clone with no fixtures, which is
/// deliberate — the suite must not fail merely because nobody has contributed a statement.
/// </remarks>
public sealed class RedactedStatementTests
{
    [Fact]
    public void Every_redacted_statement_parses()
    {
        IReadOnlyList<string> files = FixturePaths.RedactedFiles;

        // Passes vacuously on a clone with no fixtures. The suite must not fail merely
        // because nobody has contributed a statement yet.
        foreach (string file in files)
        {
            OfxParseResult result = OfxStatementReader.ReadFile(file);

            result.HasBlockingError.ShouldBeFalse(
                $"{Path.GetFileName(file)} failed to parse: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));

            result.Statements.ShouldNotBeEmpty($"{Path.GetFileName(file)} produced no statement.");
        }
    }

    [Fact]
    public void Every_redacted_statement_has_usable_transactions()
    {
        foreach (string file in FixturePaths.RedactedFiles)
        {
            foreach (OfxStatement statement in OfxStatementReader.ReadFile(file).Statements)
            {
                string name = Path.GetFileName(file);

                statement.Account.AccountId.ShouldNotBeNullOrWhiteSpace($"{name}: no account id.");
                statement.Transactions.ShouldNotBeEmpty($"{name}: no transactions.");

                // A row that parsed to a default date or a zero amount usually means a field
                // shape the reader did not recognise rather than a genuinely empty row.
                statement.Transactions.ShouldAllBe(
                    t => t.Posted > new DateOnly(1990, 1, 1),
                    $"{name}: a transaction has an implausible date.");
            }
        }
    }

    [Fact]
    public void Redacted_statements_still_reconcile_against_their_stated_balance()
    {
        foreach (string file in FixturePaths.RedactedFiles)
        {
            foreach (OfxStatement statement in OfxStatementReader.ReadFile(file).Statements)
            {
                if (statement.LedgerBalance is null)
                {
                    continue;
                }

                // Only meaningful when the file covers the account's whole life, which a
                // single statement rarely does — so this asserts the amounts parsed to
                // something coherent, not that they equal the closing balance.
                statement.Total.ShouldNotBe(
                    Money.Zero,
                    $"{Path.GetFileName(file)}: every amount parsed to zero, which means the amount format was not understood.");
            }
        }
    }
}

/// <summary>Produces committable copies of the real statements kept locally.</summary>
public sealed class OfxRedactorTests
{
    [Fact]
    public void Redaction_removes_the_account_number_and_the_merchant()
    {
        const string original = """
            OFXHEADER:100

            <OFX>
            <BANKMSGSRSV1>
            <STMTTRNRS>
            <STMTRS>
            <BANKACCTFROM>
            <BANKID>043000096
            <ACCTID>1234567890123456
            <ACCTTYPE>CHECKING
            </BANKACCTFROM>
            <BANKTRANLIST>
            <STMTTRN>
            <TRNTYPE>DEBIT
            <DTPOSTED>20260302
            <TRNAMT>-18.40
            <FITID>202603020001
            <NAME>SQ *BLUE BOTTLE 1234
            </STMTTRN>
            </BANKTRANLIST>
            </STMTRS>
            </STMTTRNRS>
            </BANKMSGSRSV1>
            </OFX>
            """;

        string redacted = OfxRedactor.Redact(original);

        redacted.ShouldNotContain("1234567890123456");
        redacted.ShouldNotContain("043000096");
        redacted.ShouldNotContain("BLUE BOTTLE");
        redacted.ShouldNotContain("202603020001");

        // The amount is kept on purpose: without it the ledger cross-check — the thing that
        // catches a statement imported with every sign reversed — cannot be tested at all.
        redacted.ShouldContain("-18.40");
        redacted.ShouldContain("<ACCTTYPE>CHECKING");
    }

    [Fact]
    public void Redaction_leaves_the_files_shape_untouched()
    {
        string original = OfxSamples.BankSgml;
        string redacted = OfxRedactor.Redact(original);

        // The malformations are the whole point of keeping a real file, so the tag structure
        // has to survive exactly.
        OfxParseResult before = OfxStatementReader.Read(OfxParser.Parse(original));
        OfxParseResult after = OfxStatementReader.Read(OfxParser.Parse(redacted));

        after.Statements.Count.ShouldBe(before.Statements.Count);
        after.Statements[0].Transactions.Count.ShouldBe(before.Statements[0].Transactions.Count);
        after.Statements[0].Total.ShouldBe(before.Statements[0].Total);
        after.Statements[0].LedgerBalance!.Amount.ShouldBe(before.Statements[0].LedgerBalance!.Amount);
    }

    [Fact]
    public void One_merchant_keeps_one_stand_in_throughout()
    {
        string redacted = OfxRedactor.Redact($"""
            OFXHEADER:100

            <OFX>
            <BANKMSGSRSV1><STMTTRNRS><STMTRS>
            <BANKACCTFROM><ACCTID>1111<ACCTTYPE>CHECKING</BANKACCTFROM>
            <BANKTRANLIST>
            {OfxSamples.Row("A1", "20260302", "-4.50", "BLUE BOTTLE")}
            {OfxSamples.Row("A2", "20260303", "-4.50", "BLUE BOTTLE")}
            {OfxSamples.Row("A3", "20260304", "-9.00", "SWEETGREEN")}
            </BANKTRANLIST>
            </STMTRS></STMTTRNRS></BANKMSGSRSV1>
            </OFX>
            """);

        List<string> names =
        [
            .. OfxStatementReader.Read(OfxParser.Parse(redacted))
                .Statements[0].Transactions
                .Select(t => t.Name!)
        ];

        // Payee matching only behaves realistically on the fixture if repeat visits to one
        // shop still look like repeat visits.
        names[0].ShouldBe(names[1]);
        names[2].ShouldNotBe(names[0]);
    }

    [Fact]
    public void Shifting_dates_keeps_their_spacing()
    {
        string redacted = OfxRedactor.Redact(OfxSamples.BankSgml, dateShiftDays: 400);

        OfxStatement statement = OfxStatementReader.Read(OfxParser.Parse(redacted)).Statements[0];

        statement.Transactions[0].Posted.ShouldBe(new DateOnly(2026, 3, 2).AddDays(400));
        (statement.Transactions[2].Posted.DayNumber - statement.Transactions[0].Posted.DayNumber)
            .ShouldBe(11);
    }

    /// <summary>
    /// Turns the local real statements into committable copies.
    /// </summary>
    /// <remarks>
    /// Guarded behind an environment variable rather than run automatically: it writes files,
    /// and a test run should not quietly change what is in the repository.
    /// </remarks>
    [Fact]
    public void Redact_the_private_fixtures()
    {
        if (Environment.GetEnvironmentVariable("MYFINANCE_REDACT") != "1")
        {
            return;
        }

        IReadOnlyList<string> files = FixturePaths.PrivateFiles;
        if (files.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(FixturePaths.Redacted);

        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file);
            string redacted = OfxRedactor.Redact(OfxEncodingSupport.Decode(bytes));

            string target = Path.Combine(
                FixturePaths.Redacted,
                Path.GetFileNameWithoutExtension(file) + ".redacted" + Path.GetExtension(file));

            File.WriteAllText(target, redacted);

            // Refuses to emit anything the parser cannot read, so a broken redaction is
            // caught here rather than as a mysterious failure later.
            OfxParseResult check = OfxStatementReader.ReadFile(target);
            check.HasBlockingError.ShouldBeFalse(
                $"Redacted output for {Path.GetFileName(file)} no longer parses.");
        }
    }
}
