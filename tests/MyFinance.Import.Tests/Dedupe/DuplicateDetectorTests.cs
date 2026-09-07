using MyFinance.Core.Primitives;
using MyFinance.Import.Dedupe;
using MyFinance.Import.Model;

namespace MyFinance.Import.Tests.Dedupe;

public sealed class DuplicateDetectorTests
{
    [Fact]
    public void A_row_the_register_has_never_seen_is_new()
    {
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [Row("A1", "2026-03-02", -18.40m, "COFFEE")],
            []);

        verdicts.Single().Kind.ShouldBe(DuplicateKind.None);
    }

    [Fact]
    public void A_matching_bank_reference_is_certain_and_cannot_be_overridden()
    {
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [Row("A1", "2026-03-02", -18.40m, "COFFEE")],
            [Existing(7, "2026-03-02", -18.40m, "A1", "Blue Bottle")]);

        DuplicateVerdict verdict = verdicts.Single();

        // Importing it anyway would violate the unique index on account and reference, which
        // would take the entire batch down rather than just this row.
        verdict.Kind.ShouldBe(DuplicateKind.ExternalId);
        verdict.ExistingId.ShouldBe(7);
        verdict.IsBlocked.ShouldBeTrue();
        verdict.ExcludedByDefault.ShouldBeTrue();
    }

    [Fact]
    public void A_reference_repeated_inside_one_file_is_kept_only_once()
    {
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [
                Row("A1", "2026-03-02", -18.40m, "COFFEE"),
                Row("A1", "2026-03-02", -18.40m, "COFFEE"),
            ],
            []);

        verdicts[0].Kind.ShouldBe(DuplicateKind.None);
        verdicts[1].Kind.ShouldBe(DuplicateKind.RepeatedInFile);
        verdicts[1].IsBlocked.ShouldBeTrue();
    }

    [Fact]
    public void A_row_with_no_reference_matching_on_date_amount_and_payee_is_likely()
    {
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [Row(null, "2026-03-02", -18.40m, "BLUE BOTTLE")],
            [Existing(7, "2026-03-03", -18.40m, null, "Blue Bottle")]);

        DuplicateVerdict verdict = verdicts.Single();

        verdict.Kind.ShouldBe(DuplicateKind.Likely);
        verdict.ExistingId.ShouldBe(7);
        verdict.ExcludedByDefault.ShouldBeTrue();
        verdict.IsBlocked.ShouldBeFalse();
    }

    [Fact]
    public void A_near_match_whose_payee_disagrees_is_only_possible_and_imports_by_default()
    {
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [Row(null, "2026-03-02", -18.40m, "SWEETGREEN")],
            [Existing(7, "2026-03-02", -18.40m, null, "Blue Bottle")]);

        DuplicateVerdict verdict = verdicts.Single();

        // Losing a real transaction is a silent error the user never notices; a spurious
        // duplicate is visible and takes one keystroke to remove.
        verdict.Kind.ShouldBe(DuplicateKind.Possible);
        verdict.ExcludedByDefault.ShouldBeFalse();
    }

    [Fact]
    public void Three_identical_amounts_in_a_week_do_not_collapse_into_one()
    {
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [
                Row(null, "2026-03-02", -4.50m, "BLUE BOTTLE"),
                Row(null, "2026-03-03", -4.50m, "BLUE BOTTLE"),
                Row(null, "2026-03-04", -4.50m, "BLUE BOTTLE"),
            ],
            [Existing(7, "2026-03-02", -4.50m, null, "Blue Bottle")]);

        // One register row can only account for one incoming row. Without that rule all three
        // coffees would match it and two real transactions would vanish.
        verdicts.Count(v => v.Kind == DuplicateKind.Likely).ShouldBe(1);
        verdicts.Count(v => v.Kind == DuplicateKind.None).ShouldBe(2);
    }

    [Fact]
    public void The_date_tolerance_has_an_edge()
    {
        IReadOnlyList<DuplicateVerdict> inside = DuplicateDetector.Detect(
            [Row(null, "2026-03-05", -18.40m, "BLUE BOTTLE")],
            [Existing(7, "2026-03-02", -18.40m, null, "Blue Bottle")]);

        inside.Single().Kind.ShouldBe(DuplicateKind.Likely);

        IReadOnlyList<DuplicateVerdict> outside = DuplicateDetector.Detect(
            [Row(null, "2026-03-06", -18.40m, "BLUE BOTTLE")],
            [Existing(7, "2026-03-02", -18.40m, null, "Blue Bottle")]);

        outside.Single().Kind.ShouldBe(DuplicateKind.None);
    }

    [Fact]
    public void A_different_amount_is_never_a_duplicate()
    {
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [Row(null, "2026-03-02", -18.41m, "BLUE BOTTLE")],
            [Existing(7, "2026-03-02", -18.40m, null, "Blue Bottle")]);

        verdicts.Single().Kind.ShouldBe(DuplicateKind.None);
    }

    [Fact]
    public void An_unseen_reference_is_new_even_when_the_amount_and_date_match()
    {
        // The bank has given this row its own id, which is stronger evidence than a
        // coincidence of amount and date.
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [Row("NEW", "2026-03-02", -18.40m, "BLUE BOTTLE")],
            [Existing(7, "2026-03-02", -18.40m, "OLD", "Blue Bottle")]);

        verdicts.Single().Kind.ShouldBe(DuplicateKind.None);
    }

    [Fact]
    public void An_overlapping_statement_imports_only_its_new_rows()
    {
        IReadOnlyList<DuplicateVerdict> verdicts = DuplicateDetector.Detect(
            [
                Row("A1", "2026-03-01", -10m, "ONE"),
                Row("A2", "2026-03-02", -20m, "TWO"),
                Row("A3", "2026-03-03", -30m, "THREE"),
            ],
            [
                Existing(1, "2026-03-01", -10m, "A1", "One"),
                Existing(2, "2026-03-02", -20m, "A2", "Two"),
            ]);

        verdicts[0].Kind.ShouldBe(DuplicateKind.ExternalId);
        verdicts[1].Kind.ShouldBe(DuplicateKind.ExternalId);
        verdicts[2].Kind.ShouldBe(DuplicateKind.None);
    }

    private static ImportedTransaction Row(string? fitId, string posted, decimal amount, string name) =>
        new()
        {
            ExternalId = fitId,
            Posted = DateOnly.Parse(posted, System.Globalization.CultureInfo.InvariantCulture),
            Amount = Money.FromDecimal(amount),
            Name = name,
        };

    private static ExistingTransaction Existing(
        int id,
        string date,
        decimal amount,
        string? fitId,
        string? payee) =>
        new(
            id,
            DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            Money.FromDecimal(amount),
            fitId,
            payee);
}
