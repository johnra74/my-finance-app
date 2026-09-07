namespace MyFinance.Import.Tests.Ofx;

/// <summary>
/// Hand-built statement files covering the shapes real banks emit.
/// </summary>
/// <remarks>
/// Written as string literals rather than checked-in files. They are reviewable in a diff,
/// carry no encoding ambiguity, and — the reason that matters most — a fixture that lives in
/// source can never accidentally be a real bank download containing somebody's account
/// number and spending history.
/// </remarks>
internal static class OfxSamples
{
    /// <summary>
    /// OFX 1.x SGML: colon-delimited header, no closing tags on any leaf. This is what the
    /// overwhelming majority of US banks still produce.
    /// </summary>
    public const string BankSgml = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII
        CHARSET:1252
        COMPRESSION:NONE
        OLDFILEUID:NONE
        NEWFILEUID:NONE

        <OFX>
        <SIGNONMSGSRSV1>
        <SONRS>
        <STATUS>
        <CODE>0
        <SEVERITY>INFO
        </STATUS>
        <DTSERVER>20260315120000[-5:EST]
        <LANGUAGE>ENG
        <FI>
        <ORG>Contoso Bank
        <FID>1234
        </FI>
        </SONRS>
        </SIGNONMSGSRSV1>
        <BANKMSGSRSV1>
        <STMTTRNRS>
        <TRNUID>1001
        <STATUS>
        <CODE>0
        <SEVERITY>INFO
        </STATUS>
        <STMTRS>
        <CURDEF>USD
        <BANKACCTFROM>
        <BANKID>043000096
        <ACCTID>1234567890123456
        <ACCTTYPE>CHECKING
        </BANKACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20260301000000
        <DTEND>20260315000000
        <STMTTRN>
        <TRNTYPE>DEBIT
        <DTPOSTED>20260302120000[-5:EST]
        <TRNAMT>-18.40
        <FITID>202603020001
        <NAME>SQ *BLUE BOTTLE 1234
        <MEMO>NEW YORK NY
        </STMTTRN>
        <STMTTRN>
        <TRNTYPE>CHECK
        <DTPOSTED>20260305120000
        <TRNAMT>-250.00
        <FITID>202603050002
        <CHECKNUM>1236
        <NAME>Landlord
        </STMTTRN>
        <STMTTRN>
        <TRNTYPE>DIRECTDEP
        <DTPOSTED>20260313120000
        <TRNAMT>3200.00
        <FITID>202603130003
        <NAME>ACME CORP PAYROLL
        </STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL>
        <BALAMT>2931.60
        <DTASOF>20260315120000
        </LEDGERBAL>
        <AVAILBAL>
        <BALAMT>2931.60
        <DTASOF>20260315120000
        </AVAILBAL>
        </STMTRS>
        </STMTTRNRS>
        </BANKMSGSRSV1>
        </OFX>
        """;

    /// <summary>OFX 2.x XML, which the same parser must read without a second code path.</summary>
    public const string CreditCardXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="no"?>
        <?OFX OFXHEADER="200" VERSION="211" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
        <OFX>
          <SIGNONMSGSRSV1>
            <SONRS>
              <STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS>
              <DTSERVER>20260315120000.000[-5:EST]</DTSERVER>
              <LANGUAGE>ENG</LANGUAGE>
              <FI><ORG>Fabrikam Card Services</ORG><FID>3101</FID></FI>
            </SONRS>
          </SIGNONMSGSRSV1>
          <CREDITCARDMSGSRSV1>
            <CCSTMTTRNRS>
              <TRNUID>0</TRNUID>
              <STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS>
              <CCSTMTRS>
                <CURDEF>USD</CURDEF>
                <CCACCTFROM><ACCTID>374245001234567</ACCTID></CCACCTFROM>
                <BANKTRANLIST>
                  <DTSTART>20260201000000.000</DTSTART>
                  <DTEND>20260228000000.000</DTEND>
                  <STMTTRN>
                    <TRNTYPE>DEBIT</TRNTYPE>
                    <DTPOSTED>20260210000000.000</DTPOSTED>
                    <TRNAMT>-42.15</TRNAMT>
                    <FITID>320260210001</FITID>
                    <NAME>AT&amp;T MOBILITY</NAME>
                  </STMTTRN>
                  <STMTTRN>
                    <TRNTYPE>CREDIT</TRNTYPE>
                    <DTPOSTED>20260220000000.000</DTPOSTED>
                    <TRNAMT>100.00</TRNAMT>
                    <FITID>320260220002</FITID>
                    <NAME>PAYMENT RECEIVED</NAME>
                  </STMTTRN>
                </BANKTRANLIST>
                <LEDGERBAL>
                  <BALAMT>-2756.30</BALAMT>
                  <DTASOF>20260228000000.000</DTASOF>
                </LEDGERBAL>
              </CCSTMTRS>
            </CCSTMTTRNRS>
          </CREDITCARDMSGSRSV1>
        </OFX>
        """;

    /// <summary>A bank refusing the request. Well-formed, zero transactions, and an error.</summary>
    public const string RejectedRequest = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102

        <OFX>
        <SIGNONMSGSRSV1>
        <SONRS>
        <STATUS>
        <CODE>15500
        <SEVERITY>ERROR
        <MESSAGE>Your credentials have expired. Please sign in online.
        </STATUS>
        </SONRS>
        </SIGNONMSGSRSV1>
        </OFX>
        """;

    /// <summary>Builds a minimal SGML statement around whatever transaction rows are given.</summary>
    public static string BankStatement(
        string transactions,
        string ledgerBalance = "0.00",
        string accountId = "1234567890",
        string accountType = "CHECKING") => $"""
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102

        <OFX>
        <BANKMSGSRSV1>
        <STMTTRNRS>
        <STMTRS>
        <CURDEF>USD
        <BANKACCTFROM>
        <BANKID>043000096
        <ACCTID>{accountId}
        <ACCTTYPE>{accountType}
        </BANKACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20260101000000
        <DTEND>20261231000000
        {transactions}
        </BANKTRANLIST>
        <LEDGERBAL>
        <BALAMT>{ledgerBalance}
        <DTASOF>20261231000000
        </LEDGERBAL>
        </STMTRS>
        </STMTTRNRS>
        </BANKMSGSRSV1>
        </OFX>
        """;

    /// <summary>One SGML transaction row, with unclosed leaves as real files write them.</summary>
    public static string Row(
        string fitId,
        string posted,
        string amount,
        string name,
        string? memo = null,
        string type = "DEBIT")
    {
        string memoLine = memo is null ? string.Empty : $"\n<MEMO>{memo}";

        return $"""
            <STMTTRN>
            <TRNTYPE>{type}
            <DTPOSTED>{posted}
            <TRNAMT>{amount}
            <FITID>{fitId}
            <NAME>{name}{memoLine}
            </STMTTRN>
            """;
    }
}
