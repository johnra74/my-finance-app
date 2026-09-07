using System.Text;
using MyFinance.Import.Ofx;

namespace MyFinance.Import.Tests.Ofx;

public sealed class OfxParserTests
{
    [Fact]
    public void An_sgml_file_with_no_closing_tags_on_its_leaves_parses()
    {
        OfxDocument document = OfxParser.Parse(OfxSamples.BankSgml);

        document.Header.MajorVersion.ShouldBe(1);
        document.Header.Get("CHARSET").ShouldBe("1252");

        OfxNode? account = document.Root.Descendants("BANKACCTFROM").Single();
        account.Text("BANKID").ShouldBe("043000096");
        account.Text("ACCTTYPE").ShouldBe("CHECKING");
    }

    [Fact]
    public void Fields_of_an_sgml_transaction_are_siblings_not_nested()
    {
        OfxDocument document = OfxParser.Parse(OfxSamples.BankSgml);
        OfxNode row = document.Root.Descendants("STMTTRN").First();

        // The heart of reading OFX 1.x. Without the implicit close of a leaf that has taken
        // text, every field would nest inside the one before it and only TRNTYPE would be
        // reachable as a direct child.
        row.Text("TRNTYPE").ShouldBe("DEBIT");
        row.Text("TRNAMT").ShouldBe("-18.40");
        row.Text("FITID").ShouldBe("202603020001");
        row.Text("NAME").ShouldBe("SQ *BLUE BOTTLE 1234");
        row.Text("MEMO").ShouldBe("NEW YORK NY");
    }

    [Fact]
    public void The_same_parser_reads_ofx_2_xml()
    {
        OfxDocument document = OfxParser.Parse(OfxSamples.CreditCardXml);

        document.Header.MajorVersion.ShouldBe(2);
        document.Header.Get("VERSION").ShouldBe("211");

        OfxNode row = document.Root.Descendants("STMTTRN").First();
        row.Text("TRNAMT").ShouldBe("-42.15");
        row.Text("NAME").ShouldBe("AT&T MOBILITY");
    }

    [Fact]
    public void An_empty_leaf_left_unclosed_does_not_swallow_the_fields_after_it()
    {
        // <MEMO> with nothing after it looks exactly like an aggregate until the enclosing
        // tag closes. Left unrepaired, TRNAMT ends up nested inside MEMO and the row reads
        // as having no amount at all.
        string file = OfxSamples.BankStatement("""
            <STMTTRN>
            <TRNTYPE>DEBIT
            <MEMO>
            <DTPOSTED>20260302
            <TRNAMT>-18.40
            <FITID>A1
            </STMTTRN>
            """);

        OfxNode row = OfxParser.Parse(file).Root.Descendants("STMTTRN").Single();

        row.Text("TRNAMT").ShouldBe("-18.40");
        row.Text("DTPOSTED").ShouldBe("20260302");
        row.Text("FITID").ShouldBe("A1");
    }

    [Fact]
    public void A_stray_closing_tag_is_ignored_rather_than_unwinding_the_document()
    {
        string file = OfxSamples.BankStatement($"""
            </NOTOPENED>
            {OfxSamples.Row("A1", "20260302", "-18.40", "COFFEE")}
            """);

        OfxDocument document = OfxParser.Parse(file);

        document.Root.Descendants("STMTTRN").Count().ShouldBe(1);
        document.Diagnostics.ShouldContain(d => d.Code == OfxDiagnostic.StrayEndTag);
    }

    [Fact]
    public void A_truncated_file_yields_the_rows_it_did_contain()
    {
        string full = OfxSamples.BankStatement($"""
            {OfxSamples.Row("A1", "20260302", "-18.40", "COFFEE")}
            {OfxSamples.Row("A2", "20260303", "-22.10", "LUNCH")}
            """);

        // Cut the file off part-way through, as an interrupted download arrives.
        string truncated = full[..full.IndexOf("A2", StringComparison.Ordinal)];

        OfxDocument document = OfxParser.Parse(truncated);

        // Giving up entirely would lose the row that did arrive intact.
        document.Root.Descendants("STMTTRN").Count().ShouldBeGreaterThanOrEqualTo(1);
        document.Root.Descendants("STMTTRN").First().Text("FITID").ShouldBe("A1");
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Every_line_ending_convention_parses(string newline)
    {
        string file = OfxSamples.BankSgml.ReplaceLineEndings(newline);

        OfxParser.Parse(file).Root.Descendants("STMTTRN").Count().ShouldBe(3);
    }

    [Fact]
    public void A_header_with_no_blank_line_before_the_body_still_parses()
    {
        string file = OfxSamples.BankSgml.Replace("\n\n<OFX>", "\n<OFX>", StringComparison.Ordinal);

        OfxDocument document = OfxParser.Parse(file);

        document.Header.Get("VERSION").ShouldBe("102");
        document.Root.Descendants("STMTTRN").Count().ShouldBe(3);
    }

    [Fact]
    public void Comments_and_self_closing_tags_are_skipped()
    {
        string file = OfxSamples.BankStatement($"""
            <!-- exported by the bank -->
            {OfxSamples.Row("A1", "20260302", "-18.40", "COFFEE")}
            <EXTENSION/>
            """);

        OfxDocument document = OfxParser.Parse(file);
        document.Root.Descendants("STMTTRN").Count().ShouldBe(1);
    }

    [Fact]
    public void A_file_with_no_markup_at_all_is_refused()
    {
        Should.Throw<OfxParseException>(() => OfxParser.Parse("this is a spreadsheet, not a statement"));
    }

    [Fact]
    public void A_well_formed_file_that_is_not_ofx_is_refused()
    {
        Should.Throw<OfxParseException>(() => OfxParser.Parse("<html><body>Sign in</body></html>"));
    }

    [Fact]
    public void A_utf8_byte_order_mark_wins_over_a_header_claiming_ascii()
    {
        // Real files do exactly this: the header says USASCII and the bytes are BOM-marked
        // UTF-8 carrying an accented merchant name.
        string text = OfxSamples.BankStatement(
            OfxSamples.Row("A1", "20260302", "-18.40", "CAFÉ RENÉ"));

        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];

        OfxNode row = OfxParser.Parse(bytes).Root.Descendants("STMTTRN").Single();
        row.Text("NAME").ShouldBe("CAFÉ RENÉ");
    }

    [Fact]
    public void Windows_1252_bytes_decode_when_the_header_declares_that_charset()
    {
        string text = OfxSamples.BankStatement(
            OfxSamples.Row("A1", "20260302", "-18.40", "SMITH’S MARKET"));

        byte[] bytes = OfxEncodingSupport.WindowsLatin1.GetBytes(text);

        OfxNode row = OfxParser.Parse(bytes).Root.Descendants("STMTTRN").Single();

        // The right single quote lives at 0x92, which is in the range where Windows-1252 and
        // Latin-1 disagree — decoding as Latin-1 would produce a control character here.
        row.Text("NAME").ShouldBe("SMITH’S MARKET");
    }

    [Fact]
    public void Whitespace_inside_a_value_is_collapsed()
    {
        string file = OfxSamples.BankStatement(
            OfxSamples.Row("A1", "20260302", "-18.40", "BLUE    BOTTLE   COFFEE"));

        OfxNode row = OfxParser.Parse(file).Root.Descendants("STMTTRN").Single();
        row.Text("NAME").ShouldBe("BLUE BOTTLE COFFEE");
    }

    [Fact]
    public void Tag_names_are_matched_without_regard_to_case()
    {
        string file = OfxSamples.BankStatement("""
            <stmttrn>
            <trntype>DEBIT
            <dtposted>20260302
            <trnamt>-18.40
            <fitid>A1
            </stmttrn>
            """);

        OfxNode row = OfxParser.Parse(file).Root.Descendants("STMTTRN").Single();
        row.Text("TRNAMT").ShouldBe("-18.40");
    }
}
