using System.Text.RegularExpressions;
using MyFinance.Core.Export;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Export;

/// <summary>
/// The export document's shape, which is a published interface from the day it ships.
/// </summary>
public class BookDocumentTests
{
    private static BookDocument Sample() => new()
    {
        ExportedUtc = "2026-09-05T12:00:00.0000000+00:00",
        Application = new ExportApplication("MyFinance", "1.0.0"),
        Accounts =
        [
            new ExportAccount
            {
                Id = 1,
                Name = "Everyday",
                Type = "Checking",
                Group = "Bank",
                OpeningBalance = ExportMoney.Text(Money.FromDecimal(250.00m)),
                OpeningBalanceMinorUnits = 25000,
                CurrencyCode = "USD",
            },
        ],
        Transactions =
        [
            new ExportTransaction
            {
                Id = 10,
                AccountId = 1,
                Date = "2026-03-01",
                Amount = ExportMoney.Text(Money.FromDecimal(-123.45m)),
                AmountMinorUnits = -12345,
                ClearedStatus = "Cleared",
                Splits =
                [
                    new ExportSplit
                    {
                        Id = 20,
                        Amount = ExportMoney.Text(Money.FromDecimal(-123.45m)),
                        AmountMinorUnits = -12345,
                    },
                ],
            },
        ],
    };

    [Fact]
    public void A_document_declares_its_format_and_version()
    {
        BookDocument document = Sample();

        document.Format.ShouldBe("myfinance-book");
        document.FormatVersion.ShouldBe(1);

        document.ToJson().ShouldContain("\"formatVersion\": 1");
    }

    [Fact]
    public void A_document_round_trips_through_its_serializer()
    {
        BookDocument restored = BookDocument.FromJson(Sample().ToJson());

        restored.Accounts.Single().Name.ShouldBe("Everyday");
        restored.Transactions.Single().Splits.Single().AmountMinorUnits.ShouldBe(-12345);
        restored.Application.Name.ShouldBe("MyFinance");
    }

    [Fact]
    public void An_amount_is_written_as_a_string_and_as_minor_units()
    {
        ExportTransaction transaction = Sample().Transactions.Single();

        // Siblings, not a nested object: a document is read by people, and
        // "amount": { "amount": … } is not a thing anyone should have to look at.
        transaction.Amount.ShouldBe("-123.45");
        transaction.AmountMinorUnits.ShouldBe(-12345);
        ExportMoney.Parse(transaction.AmountMinorUnits).ShouldBe(Money.FromDecimal(-123.45m));
    }

    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(5, "5.00")]
    [InlineData(-0.01, "-0.01")]
    [InlineData(1234567.89, "1234567.89")]
    public void An_amount_is_written_the_same_way_in_every_culture(decimal value, string expected)
    {
        // A comma decimal separator would make the document unreadable to half the readers
        // that will ever open it, and would silently change the number for the rest.
        ExportMoney.Text(Money.FromDecimal(value)).ShouldBe(expected);
    }

    [Fact]
    public void No_amount_is_serialised_as_a_json_number()
    {
        string json = Sample().ToJson();

        // Written as a bare JSON number, -123.45 becomes a binary double in most readers,
        // and the exactness the whole application is built on is lost at the very last step.
        // So: every "amount" key must carry a quoted string.
        foreach (Match match in Regex.Matches(json, "\"amount\":\\s*(.)"))
        {
            match.Groups[1].Value.ShouldBe("\"", $"an amount was written unquoted: {match.Value}");
        }

        json.ShouldContain("\"amount\": \"-123.45\"");
        json.ShouldContain("\"amountMinorUnits\": -12345");
    }

    [Fact]
    public void A_null_amount_is_absent_rather_than_written_as_null()
    {
        ExportMoney.Text((Money?)null).ShouldBeNull();
        ExportMoney.Minor(null).ShouldBeNull();
        Sample().ToJson().ShouldNotContain("null");
    }

    [Fact]
    public void An_export_from_a_newer_version_is_refused_rather_than_misread()
    {
        string json = Sample().ToJson().Replace("\"formatVersion\": 1", "\"formatVersion\": 99");

        Should.Throw<InvalidDataException>(() => BookDocument.FromJson(json))
            .Message.ShouldContain("99");
    }

    [Fact]
    public void A_file_that_is_not_a_book_export_is_refused()
    {
        string json = Sample().ToJson().Replace("myfinance-book", "something-else");

        Should.Throw<InvalidDataException>(() => BookDocument.FromJson(json));
    }

    [Fact]
    public void An_empty_document_is_refused_rather_than_read_as_an_empty_book()
    {
        Should.Throw<InvalidDataException>(() => BookDocument.FromJson("null"));
    }
}
