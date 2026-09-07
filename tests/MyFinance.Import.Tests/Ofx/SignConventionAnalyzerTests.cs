using MyFinance.Core.Primitives;
using MyFinance.Import.Model;

namespace MyFinance.Import.Tests.Ofx;

public sealed class SignConventionAnalyzerTests
{
    [Fact]
    public void A_card_statement_that_balances_as_written_is_certain()
    {
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [Row("DEBIT", -100m), Row("CREDIT", 40m)],
            ledgerBalance: Money.FromDecimal(-1060m),
            currentBalance: Money.FromDecimal(-1000m),
            ImportedAccountKind.CreditCard);

        verdict.Suggested.ShouldBe(SignPolarity.AsIs);
        verdict.Confidence.ShouldBe(SignConfidence.Certain);
        verdict.PreselectInversion.ShouldBeFalse();
        verdict.AgreesWithLedger.ShouldBeTrue();
    }

    [Fact]
    public void A_card_statement_whose_amounts_are_reversed_is_caught_by_the_balance()
    {
        // The issuer reports purchases positive. Taken as written this would move the balance
        // the wrong way by twice the statement total.
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [Row("DEBIT", 100m), Row("CREDIT", -40m)],
            ledgerBalance: Money.FromDecimal(-1060m),
            currentBalance: Money.FromDecimal(-1000m),
            ImportedAccountKind.CreditCard);

        verdict.Suggested.ShouldBe(SignPolarity.Inverted);
        verdict.Confidence.ShouldBe(SignConfidence.Certain);
        verdict.PreselectInversion.ShouldBeTrue();
        verdict.Projected.ShouldBe(Money.FromDecimal(-1060m));
        verdict.Explanation.ShouldContain("reversed");
    }

    [Fact]
    public void A_ledger_stated_the_other_way_round_is_recognised_without_inverting_the_rows()
    {
        // Some issuers report "you owe 1060" as a positive balance. The transactions are
        // still correct; only the stated balance runs the other way.
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [Row("DEBIT", -100m), Row("CREDIT", 40m)],
            ledgerBalance: Money.FromDecimal(1060m),
            currentBalance: Money.FromDecimal(-1000m),
            ImportedAccountKind.CreditCard);

        verdict.Suggested.ShouldBe(SignPolarity.AsIs);
        verdict.Confidence.ShouldBe(SignConfidence.Likely);
        verdict.PreselectInversion.ShouldBeFalse();
    }

    [Fact]
    public void On_a_new_account_the_balance_proves_nothing_so_the_types_decide()
    {
        // The commonest situation of all: a first import into an account with no history and
        // no opening balance. Both readings are consistent with some balance, so the
        // transaction types are the only evidence there is.
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [
                Row("DEBIT", 10m), Row("DEBIT", 20m), Row("DEBIT", 30m),
                Row("DEBIT", 40m), Row("DEBIT", 50m), Row("DEBIT", 60m),
            ],
            ledgerBalance: null,
            currentBalance: Money.Zero,
            ImportedAccountKind.CreditCard);

        verdict.Suggested.ShouldBe(SignPolarity.Inverted);
        verdict.Confidence.ShouldBe(SignConfidence.Likely);
    }

    [Fact]
    public void Transaction_types_agreeing_with_the_amounts_reads_as_correct()
    {
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [
                Row("DEBIT", -10m), Row("DEBIT", -20m), Row("DEBIT", -30m),
                Row("CREDIT", 40m), Row("DEBIT", -50m), Row("DEBIT", -60m),
            ],
            ledgerBalance: null,
            currentBalance: Money.Zero,
            ImportedAccountKind.CreditCard);

        verdict.Suggested.ShouldBe(SignPolarity.AsIs);
        verdict.Confidence.ShouldBe(SignConfidence.Likely);
    }

    [Fact]
    public void Too_few_typed_rows_and_the_check_abstains()
    {
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [Row("OTHER", 10m), Row("XFER", 20m), Row("OTHER", 30m)],
            ledgerBalance: null,
            currentBalance: Money.Zero,
            ImportedAccountKind.CreditCard);

        verdict.Confidence.ShouldBe(SignConfidence.Unknown);
        verdict.Suggested.ShouldBe(SignPolarity.AsIs);
    }

    [Fact]
    public void Untyped_rows_are_not_evidence_either_way()
    {
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [Row(null, 10m), Row(null, 20m), Row(null, 30m), Row(null, 40m), Row(null, 50m)],
            ledgerBalance: null,
            currentBalance: Money.Zero,
            ImportedAccountKind.CreditCard);

        verdict.Confidence.ShouldBe(SignConfidence.Unknown);
    }

    [Fact]
    public void A_bank_statement_is_never_inverted_on_a_heuristic_alone()
    {
        // Chequing statements are effectively never reversed in the wild, so acting on
        // suggestive-but-not-conclusive evidence would be all risk and no benefit.
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [
                Row("DEBIT", 10m), Row("DEBIT", 20m), Row("DEBIT", 30m),
                Row("DEBIT", 40m), Row("DEBIT", 50m), Row("DEBIT", 60m),
            ],
            ledgerBalance: null,
            currentBalance: Money.Zero,
            ImportedAccountKind.Bank);

        verdict.Suggested.ShouldBe(SignPolarity.AsIs);
        verdict.Confidence.ShouldBe(SignConfidence.Unknown);
        verdict.PreselectInversion.ShouldBeFalse();
    }

    [Fact]
    public void A_bank_statement_is_still_inverted_when_the_balance_proves_it()
    {
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [Row("DEBIT", 100m)],
            ledgerBalance: Money.FromDecimal(900m),
            currentBalance: Money.FromDecimal(1000m),
            ImportedAccountKind.Bank);

        verdict.Suggested.ShouldBe(SignPolarity.Inverted);
        verdict.Confidence.ShouldBe(SignConfidence.Certain);
    }

    [Fact]
    public void A_balance_that_matches_neither_reading_is_reported_without_blocking()
    {
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [Row("DEBIT", -100m)],
            ledgerBalance: Money.FromDecimal(5000m),
            currentBalance: Money.Zero,
            ImportedAccountKind.Bank);

        // Not matching on a first import is entirely normal — earlier history is simply
        // missing — so this must inform rather than prevent.
        verdict.Confidence.ShouldBe(SignConfidence.Unknown);
        verdict.AgreesWithLedger.ShouldBeFalse();
        verdict.Explanation.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_empty_statement_is_handled()
    {
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [],
            ledgerBalance: null,
            currentBalance: Money.FromDecimal(100m),
            ImportedAccountKind.Bank);

        verdict.Confidence.ShouldBe(SignConfidence.Unknown);
        verdict.ProjectedAsIs.ShouldBe(Money.FromDecimal(100m));
    }

    [Fact]
    public void Both_projections_are_offered_so_the_user_can_see_the_choice()
    {
        SignVerdict verdict = SignConventionAnalyzer.Analyze(
            [Row("DEBIT", -100m)],
            ledgerBalance: Money.FromDecimal(900m),
            currentBalance: Money.FromDecimal(1000m),
            ImportedAccountKind.Bank);

        verdict.ProjectedAsIs.ShouldBe(Money.FromDecimal(900m));
        verdict.ProjectedInverted.ShouldBe(Money.FromDecimal(1100m));
    }

    private static ImportedTransaction Row(string? type, decimal amount) =>
        new()
        {
            TransactionType = type,
            Posted = new DateOnly(2026, 3, 2),
            Amount = Money.FromDecimal(amount),
        };
}
