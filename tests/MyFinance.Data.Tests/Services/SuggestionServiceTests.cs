using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Import.Categorization;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// The category chain as the transaction editor uses it.
/// </summary>
/// <remarks>
/// The ordering is the whole product of this feature — which source wins decides what a user
/// sees when they open a row — so it is tested through the service the register calls rather
/// than only through <see cref="CategorySuggester" /> underneath it.
/// </remarks>
public sealed class SuggestionServiceTests
{
    private static SuggestionRequest For(string payee, decimal amount = -12m, int accountId = 1) =>
        new(accountId, payee, Money.FromDecimal(amount));

    [Fact]
    public async Task A_payee_filed_before_is_recommended_the_same_way_again()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");
        int coffee = await harness.CategoryIdAsync("Food : Coffee");

        await harness.AddTransactionAsync(account, -6m, payee: "Blue Bottle", categoryId: coffee);

        CategorySuggestion suggestion = await harness.Suggestions
            .SuggestAsync(For("Blue Bottle", accountId: account));

        suggestion.CategoryId.ShouldBe(coffee);
        suggestion.Source.ShouldBe(SuggestionSource.PayeeMemory);
        suggestion.IsCertain.ShouldBeTrue();
    }

    /// <summary>
    /// A rule is the only source the user wrote down deliberately, so it has to beat the
    /// habit recorded against the payee.
    /// </summary>
    [Fact]
    public async Task A_rule_beats_what_the_payee_was_last_filed_under()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");
        int coffee = await harness.CategoryIdAsync("Food : Coffee");
        int groceries = await harness.CategoryIdAsync("Food : Groceries");

        await harness.AddTransactionAsync(account, -6m, payee: "Blue Bottle", categoryId: coffee);

        await harness.Rules.CreateAsync(new RuleDraft
        {
            Name = "Blue Bottle is groceries",
            MatchField = RuleMatchField.Payee,
            MatchKind = RuleMatchKind.Contains,
            Pattern = "Blue Bottle",
            TargetCategoryId = groceries,
        });

        CategorySuggestion suggestion = await harness.Suggestions
            .SuggestAsync(For("Blue Bottle", accountId: account));

        suggestion.CategoryId.ShouldBe(groceries);
        suggestion.Source.ShouldBe(SuggestionSource.Rule);
    }

    /// <summary>
    /// A payee never seen before, with words the classifier has: a guess, and it must be
    /// marked as one so the editor offers rather than fills.
    /// </summary>
    [Fact]
    public async Task An_unknown_payee_with_familiar_words_is_a_guess_not_a_certainty()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");
        int coffee = await harness.CategoryIdAsync("Food : Coffee");
        int fuel = await harness.CategoryIdAsync("Transport : Fuel");

        // Enough history for the classifier to be worth consulting at all.
        for (int i = 0; i < 12; i++)
        {
            await harness.AddTransactionAsync(account, -6m, payee: $"Starbucks Coffee {i}", categoryId: coffee);
            await harness.AddTransactionAsync(account, -40m, payee: $"Shell Fuel {i}", categoryId: fuel);
        }

        CategorySuggestion suggestion = await harness.Suggestions
            .SuggestAsync(For("Starbucks Coffee 99", accountId: account));

        suggestion.Source.ShouldBe(SuggestionSource.Statistical);
        suggestion.CategoryId.ShouldBe(coffee);

        // The line the editor uses to decide fill-or-offer.
        suggestion.IsCertain.ShouldBeFalse();
    }

    [Fact]
    public async Task A_new_book_with_nothing_to_learn_from_invents_nothing()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");

        CategorySuggestion suggestion = await harness.Suggestions
            .SuggestAsync(For("Somewhere Entirely New", accountId: account));

        suggestion.Source.ShouldBe(SuggestionSource.None);
        suggestion.CategoryId.ShouldBeNull();
        suggestion.HasCategory.ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_payee_is_not_worth_asking_about()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");

        (await harness.Suggestions.SuggestAsync(For("   ", accountId: account)))
            .Source.ShouldBe(SuggestionSource.None);
    }

    /// <summary>
    /// The reason this service exists: training reads every categorized transaction, so it
    /// must not happen again for each suggestion.
    /// </summary>
    [Fact]
    public async Task The_model_is_built_once_and_reused()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");
        int coffee = await harness.CategoryIdAsync("Food : Coffee");

        await harness.AddTransactionAsync(account, -6m, payee: "Blue Bottle", categoryId: coffee);

        SuggestionContext first = await harness.Suggestions.GetContextAsync();
        SuggestionContext second = await harness.Suggestions.GetContextAsync();

        second.ShouldBeSameAs(first);
        second.Classifier.ShouldBeSameAs(first.Classifier);
    }

    /// <summary>
    /// The failure this design has to rule out: a cached model that has stopped matching the
    /// book recommends from history that no longer exists, and nothing about it looks wrong.
    /// </summary>
    [Fact]
    public async Task The_model_is_rebuilt_after_a_transaction_is_added()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");
        int coffee = await harness.CategoryIdAsync("Food : Coffee");

        SuggestionContext before = await harness.Suggestions.GetContextAsync();

        await harness.AddTransactionAsync(account, -6m, payee: "Blue Bottle", categoryId: coffee);

        SuggestionContext after = await harness.Suggestions.GetContextAsync();

        after.ShouldNotBeSameAs(before);
        after.Classifier.TrainingSize.ShouldBeGreaterThan(before.Classifier.TrainingSize);
    }

    [Fact]
    public async Task The_model_is_rebuilt_after_a_rule_is_written()
    {
        using var harness = new BookHarness();
        await harness.AddAccountAsync("Checking");
        int coffee = await harness.CategoryIdAsync("Food : Coffee");

        SuggestionContext before = await harness.Suggestions.GetContextAsync();

        await harness.Rules.CreateAsync(new RuleDraft
        {
            Name = "Coffee",
            Pattern = "Blue Bottle",
            TargetCategoryId = coffee,
        });

        SuggestionContext after = await harness.Suggestions.GetContextAsync();

        after.Rules.Count.ShouldBe(before.Rules.Count + 1);
    }

    /// <summary>
    /// Re-categorizing rewrites the row's split without changing any count, which is the case
    /// a count-only probe would miss.
    /// </summary>
    [Fact]
    public async Task The_model_is_rebuilt_after_a_transaction_is_recategorized()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");
        int coffee = await harness.CategoryIdAsync("Food : Coffee");
        int groceries = await harness.CategoryIdAsync("Food : Groceries");

        int id = await harness.AddTransactionAsync(account, -6m, payee: "Blue Bottle", categoryId: coffee);

        await harness.Suggestions.GetContextAsync();

        await harness.Register.SaveAsync(new TransactionDraft
        {
            Id = id,
            AccountId = account,
            Date = new DateOnly(2026, 1, 15),
            Amount = Money.FromDecimal(-6m),
            PayeeName = "Blue Bottle",
            Splits = [new SplitDraft { CategoryId = groceries, Amount = Money.FromDecimal(-6m) }],
        });

        CategorySuggestion suggestion = await harness.Suggestions
            .SuggestAsync(For("Blue Bottle", accountId: account));

        suggestion.CategoryId.ShouldBe(groceries);
    }

    [Fact]
    public async Task Every_source_describes_itself_in_words_a_person_can_read()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Checking");
        int coffee = await harness.CategoryIdAsync("Food : Coffee");

        await harness.AddTransactionAsync(account, -6m, payee: "Blue Bottle", categoryId: coffee);

        CategorySuggestion suggestion = await harness.Suggestions
            .SuggestAsync(For("Blue Bottle", accountId: account));

        suggestion.Describe().ShouldNotBeNullOrWhiteSpace();
        suggestion.Describe().ShouldBe("Usual for this payee");
    }
}
