using MyFinance.Core.Enums;
using MyFinance.Data.Services;
using MyFinance.Import.Categorization;

namespace MyFinance.Data.Tests.Services;

public sealed class CategorizationRuleServiceTests
{
    [Fact]
    public async Task A_rule_can_be_created_and_read_back()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        int id = await book.Rules.CreateAsync(new RuleDraft
        {
            Name = "Shell is fuel",
            MatchField = RuleMatchField.PayeeOrMemo,
            MatchKind = RuleMatchKind.Contains,
            Pattern = "SHELL",
            TargetCategoryId = fuel,
        });

        RuleListItem saved = (await book.Rules.GetAllAsync()).Single();

        saved.Id.ShouldBe(id);
        saved.Name.ShouldBe("Shell is fuel");
        saved.Condition.ShouldBe("Payee or description contains \"SHELL\"");
        saved.Action.ShouldBe("file under Transport : Fuel");
        saved.ScopeText.ShouldBe("All accounts");
        saved.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_rule_that_does_nothing_is_refused()
    {
        using var book = new BookHarness();

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Rules.CreateAsync(new RuleDraft { Name = "Pointless", Pattern = "SHELL" }));

        thrown.Errors.ShouldContain(e => e.Code == CategorizationRuleService.NoAction);
    }

    [Fact]
    public async Task An_invalid_regular_expression_is_refused_at_the_point_of_saving()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Rules.CreateAsync(new RuleDraft
            {
                Name = "Broken",
                MatchKind = RuleMatchKind.Regex,
                Pattern = "([unclosed",
                TargetCategoryId = fuel,
            }));

        // Better here than as a rule that silently never matches anything.
        thrown.Errors.ShouldContain(e => e.Code == CategorizationRuleService.PatternInvalid);
    }

    [Fact]
    public async Task A_nameless_rule_is_refused()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Rules.CreateAsync(new RuleDraft { Pattern = "SHELL", TargetCategoryId = fuel }));

        thrown.Errors.ShouldContain(e => e.Code == CategorizationRuleService.NameRequired);
    }

    [Fact]
    public async Task New_rules_go_to_the_end_of_the_order()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        int first = await book.AddRuleAsync("First", "A", fuel);
        int second = await book.AddRuleAsync("Second", "B", fuel);

        // Adding a rule must never silently change what an existing one does by leaping
        // ahead of it.
        IReadOnlyList<RuleListItem> rules = await book.Rules.GetAllAsync();
        rules.Select(r => r.Id).ShouldBe([first, second]);
        rules[1].Priority.ShouldBeGreaterThan(rules[0].Priority);
    }

    [Fact]
    public async Task Rules_can_be_reordered()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        int first = await book.AddRuleAsync("First", "A", fuel);
        int second = await book.AddRuleAsync("Second", "B", fuel);
        int third = await book.AddRuleAsync("Third", "C", fuel);

        await book.Rules.MoveAsync(third, -2);

        (await book.Rules.GetAllAsync()).Select(r => r.Id).ShouldBe([third, first, second]);
    }

    [Fact]
    public async Task Moving_past_the_end_stops_at_the_end()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        int first = await book.AddRuleAsync("First", "A", fuel);
        int second = await book.AddRuleAsync("Second", "B", fuel);

        await book.Rules.MoveAsync(first, 99);

        (await book.Rules.GetAllAsync()).Select(r => r.Id).ShouldBe([second, first]);
    }

    [Fact]
    public async Task A_rule_can_be_disabled_without_losing_it()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");
        int id = await book.AddRuleAsync("Shell", "SHELL", fuel);

        await book.Rules.SetEnabledAsync(id, isEnabled: false);

        (await book.Rules.GetAllAsync()).Single().IsEnabled.ShouldBeFalse();

        // Disabled rules are skipped by the evaluator but kept in the list.
        (await book.Rules.GetSpecsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rule_can_be_deleted()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");
        int id = await book.AddRuleAsync("Shell", "SHELL", fuel);

        await book.Rules.DeleteAsync(id);

        (await book.Rules.GetAllAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rule_can_be_tried_against_the_existing_register_before_it_is_saved()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        await book.AddTransactionAsync(account, -40m, payee: "Shell Oil");
        await book.AddTransactionAsync(account, -35m, payee: "Shell Oil");
        await book.AddTransactionAsync(account, -12m, payee: "Blue Bottle");

        int matches = await book.Rules.CountMatchesAsync(new RuleDraft
        {
            Name = "Shell",
            MatchField = RuleMatchField.Payee,
            MatchKind = RuleMatchKind.Contains,
            Pattern = "Shell",
            TargetCategoryId = fuel,
        });

        // The way to find out whether a rule says what you meant before letting it loose on
        // an import.
        matches.ShouldBe(2);
    }

    [Fact]
    public async Task Trying_a_broken_expression_reports_nothing_rather_than_throwing()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        await book.AddTransactionAsync(account, -40m, payee: "Shell Oil");

        int matches = await book.Rules.CountMatchesAsync(new RuleDraft
        {
            Name = "Broken",
            MatchKind = RuleMatchKind.Regex,
            Pattern = "([unclosed",
        });

        matches.ShouldBe(0);
    }

    [Fact]
    public async Task An_amount_rule_reads_as_plain_english()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Financial : Bank charges");

        await book.Rules.CreateAsync(new RuleDraft
        {
            Name = "Large withdrawals",
            MatchField = RuleMatchField.Amount,
            Pattern = "< -1000",
            TargetCategoryId = fuel,
        });

        (await book.Rules.GetAllAsync()).Single().Condition.ShouldBe("Amount is < -1000");
    }

    [Fact]
    public async Task A_rule_restricted_to_one_account_says_so()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Rewards Card", AccountType.CreditCard);
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        await book.Rules.CreateAsync(new RuleDraft
        {
            Name = "Card only",
            Pattern = "SHELL",
            AccountId = account,
            TargetCategoryId = fuel,
        });

        (await book.Rules.GetAllAsync()).Single().ScopeText.ShouldBe("Rewards Card");
    }

    [Fact]
    public async Task The_specs_handed_to_the_evaluator_come_back_in_priority_order()
    {
        using var book = new BookHarness();
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        int first = await book.AddRuleAsync("First", "A", fuel);
        int second = await book.AddRuleAsync("Second", "B", fuel);
        await book.Rules.MoveAsync(second, -1);

        IReadOnlyList<RuleSpec> specs = await book.Rules.GetSpecsAsync();
        specs.Select(r => r.Id).ShouldBe([second, first]);
    }
}
