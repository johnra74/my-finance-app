using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Model;
using MyFinance.Import.Ofx;
using MyFinance.Import.Ofx.Model;

namespace MyFinance.Import.Tests.Ofx;

public sealed class OfxStatementReaderTests
{
    [Fact]
    public void A_bank_statement_is_read_whole()
    {
        OfxParseResult result = Read(OfxSamples.BankSgml);

        result.HasBlockingError.ShouldBeFalse();
        OfxStatement statement = result.Statements.Single();

        statement.Account.Kind.ShouldBe(OfxAccountKind.Bank);
        statement.Account.BankId.ShouldBe("043000096");
        statement.Account.AccountId.ShouldBe("1234567890123456");
        statement.Account.MappedType.ShouldBe(AccountType.Checking);
        statement.Institution.ShouldBe("Contoso Bank");
        statement.CurrencyCode.ShouldBe("USD");
        statement.PeriodStart.ShouldBe(new DateOnly(2026, 3, 1));
        statement.PeriodEnd.ShouldBe(new DateOnly(2026, 3, 15));
        statement.Transactions.Count.ShouldBe(3);
        statement.LedgerBalance!.Amount.ShouldBe(Money.FromDecimal(2931.60m));
    }

    [Fact]
    public void Transaction_fields_survive_the_journey()
    {
        OfxTransaction first = Read(OfxSamples.BankSgml).Statements.Single().Transactions[0];

        first.FitId.ShouldBe("202603020001");
        first.TransactionType.ShouldBe("DEBIT");
        first.Posted.ShouldBe(new DateOnly(2026, 3, 2));
        first.Amount.ShouldBe(Money.FromDecimal(-18.40m));
        first.Name.ShouldBe("SQ *BLUE BOTTLE 1234");
        first.Memo.ShouldBe("NEW YORK NY");
        first.RawDescriptor.ShouldBe("SQ *BLUE BOTTLE 1234 NEW YORK NY");
    }

    [Fact]
    public void A_cheque_number_is_carried_across()
    {
        OfxTransaction cheque = Read(OfxSamples.BankSgml).Statements.Single().Transactions[1];

        cheque.CheckNumber.ShouldBe("1236");
        cheque.Amount.ShouldBe(Money.FromDecimal(-250m));
    }

    [Fact]
    public void A_credit_card_statement_is_recognised_as_one()
    {
        OfxStatement statement = Read(OfxSamples.CreditCardXml).Statements.Single();

        statement.Account.Kind.ShouldBe(OfxAccountKind.CreditCard);
        statement.Account.MappedType.ShouldBe(AccountType.CreditCard);
        statement.Account.AccountId.ShouldBe("374245001234567");
        statement.Institution.ShouldBe("Fabrikam Card Services");
        statement.LedgerBalance!.Amount.ShouldBe(Money.FromDecimal(-2756.30m));
        statement.Transactions[0].Name.ShouldBe("AT&T MOBILITY");
    }

    [Theory]
    [InlineData("CHECKING", AccountType.Checking)]
    [InlineData("SAVINGS", AccountType.Savings)]
    [InlineData("MONEYMRKT", AccountType.MoneyMarket)]
    [InlineData("CD", AccountType.CertificateOfDeposit)]
    [InlineData("CREDITLINE", AccountType.LineOfCredit)]
    public void Account_types_map_onto_ours(string ofxType, AccountType expected)
    {
        string file = OfxSamples.BankStatement(
            OfxSamples.Row("A1", "20260302", "-18.40", "COFFEE"),
            accountType: ofxType);

        Read(file).Statements.Single().Account.MappedType.ShouldBe(expected);
    }

    [Fact]
    public void An_unknown_account_type_maps_to_nothing_rather_than_guessing()
    {
        string file = OfxSamples.BankStatement(
            OfxSamples.Row("A1", "20260302", "-18.40", "COFFEE"),
            accountType: "SOMETHINGNEW");

        Read(file).Statements.Single().Account.MappedType.ShouldBeNull();
    }

    [Fact]
    public void A_rejected_request_surfaces_the_banks_own_message()
    {
        OfxParseResult result = Read(OfxSamples.RejectedRequest);

        // The file is well-formed and contains no transactions. Reporting "0 transactions
        // found" would make the user think this application is at fault.
        result.HasBlockingError.ShouldBeTrue();
        result.Statements.ShouldBeEmpty();

        ImportDiagnostic error = result.Errors.Single();
        error.Code.ShouldBe(OfxDiagnostic.BankStatus);
        error.Message.ShouldContain("15500");
        error.Message.ShouldContain("Your credentials have expired");
    }

    [Fact]
    public void A_warning_status_does_not_stop_the_import()
    {
        string file = OfxSamples.BankSgml.Replace(
            """
            <CODE>0
            <SEVERITY>INFO
            </STATUS>
            <DTSERVER>
            """,
            """
            <CODE>2002
            <SEVERITY>WARN
            <MESSAGE>Some transactions may be delayed.
            </STATUS>
            <DTSERVER>
            """,
            StringComparison.Ordinal);

        OfxParseResult result = Read(file);

        result.HasBlockingError.ShouldBeFalse();
        result.Statements.Single().Transactions.Count.ShouldBe(3);
        result.Warnings.ShouldContain(d => d.Message.Contains("delayed", StringComparison.Ordinal));
    }

    [Fact]
    public void An_investment_statement_is_refused_rather_than_partly_read()
    {
        string file = """
            OFXHEADER:100
            DATA:OFXSGML

            <OFX>
            <INVSTMTMSGSRSV1>
            <INVSTMTTRNRS>
            <INVSTMTRS>
            <INVACCTFROM>
            <BROKERID>vanguard.com
            <ACCTID>12345678
            </INVACCTFROM>
            <INVTRANLIST>
            <INVBANKTRAN>
            <STMTTRN>
            <TRNTYPE>CREDIT
            <DTPOSTED>20260302
            <TRNAMT>100.00
            <FITID>V1
            </STMTTRN>
            </INVBANKTRAN>
            </INVTRANLIST>
            </INVSTMTRS>
            </INVSTMTTRNRS>
            </INVSTMTMSGSRSV1>
            </OFX>
            """;

        OfxParseResult result = Read(file);

        // Importing the cash legs while silently dropping every buy and sell would produce a
        // register that looks complete and is wrong.
        result.Statements.ShouldBeEmpty();
        result.Diagnostics.ShouldContain(d => d.Code == OfxDiagnostic.InvestmentUnsupported);
    }

    [Fact]
    public void Several_statements_in_one_file_are_all_returned()
    {
        string file = $"""
            OFXHEADER:100
            DATA:OFXSGML

            <OFX>
            <BANKMSGSRSV1>
            <STMTTRNRS>
            <STMTRS>
            <CURDEF>USD
            <BANKACCTFROM>
            <BANKID>1
            <ACCTID>1111
            <ACCTTYPE>CHECKING
            </BANKACCTFROM>
            <BANKTRANLIST>
            {OfxSamples.Row("A1", "20260302", "-18.40", "COFFEE")}
            </BANKTRANLIST>
            </STMTRS>
            </STMTTRNRS>
            <STMTTRNRS>
            <STMTRS>
            <CURDEF>USD
            <BANKACCTFROM>
            <BANKID>1
            <ACCTID>2222
            <ACCTTYPE>SAVINGS
            </BANKACCTFROM>
            <BANKTRANLIST>
            {OfxSamples.Row("B1", "20260302", "500.00", "TRANSFER", type: "CREDIT")}
            </BANKTRANLIST>
            </STMTRS>
            </STMTTRNRS>
            </BANKMSGSRSV1>
            </OFX>
            """;

        OfxParseResult result = Read(file);

        result.Statements.Count.ShouldBe(2);
        result.Statements[0].Account.AccountId.ShouldBe("1111");
        result.Statements[1].Account.AccountId.ShouldBe("2222");
        result.Statements[1].Account.MappedType.ShouldBe(AccountType.Savings);
    }

    [Fact]
    public void A_row_with_an_unreadable_date_is_skipped_and_reported()
    {
        string file = OfxSamples.BankStatement($"""
            {OfxSamples.Row("A1", "20260302", "-18.40", "COFFEE")}
            {OfxSamples.Row("A2", "GARBAGE", "-22.10", "LUNCH")}
            """);

        OfxParseResult result = Read(file);

        // The other three hundred and ninety-nine rows of a statement should still import.
        result.Statements.Single().Transactions.Count.ShouldBe(1);
        result.Diagnostics.ShouldContain(d => d.Code == OfxDiagnostic.UnreadableDate);
    }

    [Fact]
    public void A_row_with_an_unreadable_amount_is_skipped_and_reported()
    {
        string file = OfxSamples.BankStatement($"""
            {OfxSamples.Row("A1", "20260302", "-18.40", "COFFEE")}
            {OfxSamples.Row("A2", "20260303", "TWENTY DOLLARS", "LUNCH")}
            """);

        OfxParseResult result = Read(file);

        result.Statements.Single().Transactions.Count.ShouldBe(1);
        result.Diagnostics.ShouldContain(d => d.Code == OfxDiagnostic.UnreadableAmount);
    }

    [Fact]
    public void A_bank_issued_correction_is_reported_but_not_applied()
    {
        string file = OfxSamples.BankStatement("""
            <STMTTRN>
            <TRNTYPE>DEBIT
            <DTPOSTED>20260302
            <TRNAMT>-18.40
            <FITID>A2
            <CORRECTFITID>A1
            <CORRECTACTION>REPLACE
            <NAME>COFFEE
            </STMTTRN>
            """);

        OfxParseResult result = Read(file);

        // Auto-replacing would throw away the category and payee the user had put on the
        // original row. Tell them instead.
        result.Statements.Single().Transactions.Single().CorrectsFitId.ShouldBe("A1");
        result.Diagnostics.ShouldContain(d => d.Code == OfxDiagnostic.CorrectedTransaction);
    }

    [Fact]
    public void A_file_with_no_statement_at_all_is_reported_as_an_error()
    {
        OfxParseResult result = Read("""
            OFXHEADER:100

            <OFX>
            <SIGNONMSGSRSV1>
            <SONRS>
            <DTSERVER>20260315
            </SONRS>
            </SIGNONMSGSRSV1>
            </OFX>
            """);

        result.HasBlockingError.ShouldBeTrue();
        result.Errors.ShouldContain(d => d.Code == OfxDiagnostic.NoStatements);
    }

    [Fact]
    public void A_statement_totals_its_rows_as_stated()
    {
        OfxStatement statement = Read(OfxSamples.BankSgml).Statements.Single();

        statement.Total.ShouldBe(Money.FromDecimal(2931.60m));
        statement.FirstPosted.ShouldBe(new DateOnly(2026, 3, 2));
        statement.LastPosted.ShouldBe(new DateOnly(2026, 3, 13));
    }

    private static OfxParseResult Read(string text) =>
        OfxStatementReader.Read(OfxParser.Parse(text));
}
