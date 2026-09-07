using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Categorization;

namespace MyFinance.Import.Tests.Categorization;

public sealed class RuleEvaluatorTests
{
    [Fact]
    public void A_payee_rule_fires_on_the_payee()
    {
        RuleSpec rule = Rule(pattern: "Shell", categoryId: 7);

        RuleEvaluator.Matches(rule, Input(payee: "Shell Oil")).ShouldBeTrue();
        RuleEvaluator.Matches(rule, Input(payee: "Chevron")).ShouldBeFalse();
    }

    [Fact]
    public void Matching_ignores_case_unless_told_otherwise()
    {
        RuleEvaluator.Matches(Rule(pattern: "shell"), Input(payee: "SHELL OIL")).ShouldBeTrue();

        RuleEvaluator
            .Matches(Rule(pattern: "shell", caseSensitive: true), Input(payee: "SHELL OIL"))
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(RuleMatchKind.Contains, "BOTTLE", true)]
    [InlineData(RuleMatchKind.Equals, "BLUE BOTTLE COFFEE", true)]
    [InlineData(RuleMatchKind.Equals, "BOTTLE", false)]
    [InlineData(RuleMatchKind.StartsWith, "BLUE", true)]
    [InlineData(RuleMatchKind.StartsWith, "BOTTLE", false)]
    [InlineData(RuleMatchKind.EndsWith, "COFFEE", true)]
    [InlineData(RuleMatchKind.Regex, "^BLUE.*COFFEE$", true)]
    [InlineData(RuleMatchKind.Regex, "^SWEET", false)]
    public void Every_comparison_kind_works(RuleMatchKind kind, string pattern, bool expected) =>
        RuleEvaluator
            .Matches(Rule(kind: kind, pattern: pattern), Input(payee: "BLUE BOTTLE COFFEE"))
            .ShouldBe(expected);

    [Fact]
    public void A_memo_rule_looks_at_the_descriptor_not_the_payee()
    {
        RuleSpec rule = Rule(field: RuleMatchField.Memo, pattern: "NEW YORK");

        RuleEvaluator.Matches(rule, Input(payee: "Blue Bottle", memo: "SQ *BLUE BOTTLE NEW YORK NY"))
            .ShouldBeTrue();

        RuleEvaluator.Matches(rule, Input(payee: "NEW YORK TIMES", memo: "SUBSCRIPTION"))
            .ShouldBeFalse();
    }

    [Fact]
    public void A_combined_rule_looks_at_either()
    {
        RuleSpec rule = Rule(field: RuleMatchField.PayeeOrMemo, pattern: "COFFEE");

        RuleEvaluator.Matches(rule, Input(payee: "COFFEE SHOP", memo: "x")).ShouldBeTrue();
        RuleEvaluator.Matches(rule, Input(payee: "x", memo: "COFFEE BEANS")).ShouldBeTrue();
        RuleEvaluator.Matches(rule, Input(payee: "x", memo: "y")).ShouldBeFalse();
    }

    [Fact]
    public void An_account_restricted_rule_only_fires_on_that_account()
    {
        RuleSpec rule = Rule(pattern: "Shell", accountId: 3);

        RuleEvaluator.Matches(rule, Input(accountId: 3, payee: "Shell")).ShouldBeTrue();
        RuleEvaluator.Matches(rule, Input(accountId: 4, payee: "Shell")).ShouldBeFalse();
    }

    [Fact]
    public void A_disabled_rule_never_fires()
    {
        RuleEvaluator
            .Matches(Rule(pattern: "Shell", enabled: false), Input(payee: "Shell"))
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("> 100", 150, true)]
    [InlineData("> 100", 50, false)]
    [InlineData(">= 100", 100, true)]
    [InlineData("< -50", -100, true)]
    [InlineData("< -50", -10, false)]
    [InlineData("<= -50", -50, true)]
    [InlineData("= 18.40", 18.40, true)]
    [InlineData("18.40", 18.40, true)]
    [InlineData("<> 18.40", 20, true)]
    [InlineData("10..50", 25, true)]
    [InlineData("10..50", 75, false)]
    [InlineData("50..10", 25, true)]
    public void Amount_conditions_compare_against_the_signed_amount(
        string pattern,
        decimal amount,
        bool expected) =>
        RuleEvaluator.MatchesAmount(pattern, Money.FromDecimal(amount)).ShouldBe(expected);

    [Fact]
    public void An_amount_rule_reads_money_out_as_negative()
    {
        // The convention everywhere else in the application, so "< -100" means "more than a
        // hundred going out". It reads oddly the first time and any other choice would be
        // inconsistent with the register.
        RuleSpec rule = Rule(field: RuleMatchField.Amount, pattern: "< -100");

        RuleEvaluator.Matches(rule, Input(amount: -250m)).ShouldBeTrue();
        RuleEvaluator.Matches(rule, Input(amount: 250m)).ShouldBeFalse();
    }

    [Fact]
    public void The_first_matching_rule_wins_in_priority_order()
    {
        List<RuleSpec> rules =
        [
            Rule(id: 1, priority: 10, pattern: "SHELL", categoryId: 100, name: "General fuel"),
            Rule(id: 2, priority: 1, pattern: "SHELL 4471", categoryId: 200, name: "Commute fuel"),
        ];

        RuleMatch? match = RuleEvaluator.FirstMatch(rules, Input(payee: "SHELL 4471"));

        // A specific rule placed above a general one overrides it, which is what anyone who
        // has used a mail filter expects.
        match.ShouldNotBeNull();
        match.CategoryId.ShouldBe(200);
        match.Rule.Name.ShouldBe("Commute fuel");
    }

    [Fact]
    public void Rules_of_equal_priority_break_the_tie_on_age()
    {
        List<RuleSpec> rules =
        [
            Rule(id: 5, priority: 1, pattern: "SHELL", categoryId: 500),
            Rule(id: 2, priority: 1, pattern: "SHELL", categoryId: 200),
        ];

        RuleEvaluator.FirstMatch(rules, Input(payee: "SHELL"))!.CategoryId.ShouldBe(200);
    }

    [Fact]
    public void Nothing_matching_returns_nothing()
    {
        RuleEvaluator.FirstMatch([Rule(pattern: "SHELL")], Input(payee: "COSTCO")).ShouldBeNull();
    }

    [Fact]
    public void A_broken_regular_expression_does_not_stop_the_import()
    {
        // A rule can be edited to something invalid, or predate validation. One bad rule is a
        // mistake in that rule, not a reason to refuse a four-hundred-row statement.
        RuleSpec rule = Rule(kind: RuleMatchKind.Regex, pattern: "([unclosed");

        Should.NotThrow(() => RuleEvaluator.Matches(rule, Input(payee: "anything")));
        RuleEvaluator.Matches(rule, Input(payee: "anything")).ShouldBeFalse();
    }

    [Fact]
    public void A_rule_may_rewrite_the_payee_without_naming_a_category()
    {
        RuleSpec rule = Rule(pattern: "SQ *BLUE", categoryId: null, payeeId: 42);

        RuleMatch? match = RuleEvaluator.FirstMatch([rule], Input(payee: "SQ *BLUE BOTTLE"));

        match.ShouldNotBeNull();
        match.CategoryId.ShouldBeNull();
        match.PayeeId.ShouldBe(42);
    }

    [Theory]
    [InlineData(RuleMatchKind.Contains, "shell", null)]
    [InlineData(RuleMatchKind.Regex, "^SHELL", null)]
    [InlineData(RuleMatchKind.Regex, "([unclosed", "not a valid")]
    [InlineData(RuleMatchKind.Contains, "", "needs something")]
    [InlineData(RuleMatchKind.Contains, "   ", "needs something")]
    public void Patterns_are_checked_before_they_are_saved(
        RuleMatchKind kind,
        string pattern,
        string? expectedFragment)
    {
        string? problem = RuleEvaluator.Validate(kind, pattern);

        if (expectedFragment is null)
        {
            problem.ShouldBeNull();
        }
        else
        {
            problem.ShouldNotBeNull();
            problem.ShouldContain(expectedFragment);
        }
    }

    [Fact]
    public void Disabled_rules_are_left_out_of_the_evaluation_order()
    {
        List<RuleSpec> rules =
        [
            Rule(id: 1, priority: 1, enabled: false),
            Rule(id: 2, priority: 2),
        ];

        RuleEvaluator.InEvaluationOrder(rules).Select(r => r.Id).ShouldBe([2]);
    }

    private static RuleSpec Rule(
        int id = 1,
        string name = "Test rule",
        int priority = 0,
        RuleMatchField field = RuleMatchField.Payee,
        RuleMatchKind kind = RuleMatchKind.Contains,
        string pattern = "PATTERN",
        bool caseSensitive = false,
        int? accountId = null,
        int? categoryId = 1,
        int? payeeId = null,
        bool enabled = true) =>
        new(id, name, priority, field, kind, pattern, caseSensitive, accountId, categoryId, payeeId, enabled);

    private static RuleInput Input(
        int accountId = 1,
        string? payee = null,
        string? memo = null,
        decimal amount = -10m) =>
        new(accountId, payee, memo, Money.FromDecimal(amount));
}
