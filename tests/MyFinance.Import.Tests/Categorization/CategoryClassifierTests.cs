using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Categorization;

namespace MyFinance.Import.Tests.Categorization;

public sealed class TextTokenizerTests
{
    [Fact]
    public void Text_is_lower_cased_and_split_on_punctuation()
    {
        TextTokenizer.Tokenize("SQ *Blue-Bottle, Coffee")
            .ShouldBe(["sq", "blue", "bottle", "coffee"]);
    }

    [Fact]
    public void Store_numbers_are_dropped_rather_than_learned_as_noise()
    {
        // The number differs on every visit to the same shop, so keeping it would teach the
        // model a word it will never see again.
        TextTokenizer.Tokenize("BLUE BOTTLE 1234").ShouldBe(["blue", "bottle"]);
    }

    [Fact]
    public void Single_characters_and_common_words_carry_no_signal()
    {
        TextTokenizer.Tokenize("A payment to THE Coffee Co")
            .ShouldBe(["coffee"]);
    }

    [Fact]
    public void A_word_with_digits_in_it_survives()
    {
        // "7eleven" is a name; "1234" on its own is a reference.
        TextTokenizer.Tokenize("7-ELEVEN 34212").ShouldBe(["eleven"]);
        TextTokenizer.Tokenize("STORE7 MAIN").ShouldBe(["store7", "main"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! 123 ###")]
    public void Nothing_meaningful_yields_no_tokens(string? input) =>
        TextTokenizer.Tokenize(input).ShouldBeEmpty();
}

public sealed class CategoryClassifierTests
{
    private const int Coffee = 10;
    private const int Fuel = 20;
    private const int Groceries = 30;

    [Fact]
    public void An_untrained_classifier_never_suggests_anything()
    {
        CategoryClassifier.Empty.Predict("Blue Bottle Coffee").ShouldBeNull();
        CategoryClassifier.Empty.IsUseful.ShouldBeFalse();
    }

    [Fact]
    public void A_book_with_too_little_history_is_left_alone()
    {
        // On a new book the model would be guessing from a handful of rows, which is worse
        // than saying nothing.
        CategoryClassifier classifier = CategoryClassifier.Train(
        [
            new TrainingExample("Blue Bottle Coffee", Coffee),
            new TrainingExample("Blue Bottle Coffee", Coffee),
        ]);

        classifier.IsUseful.ShouldBeFalse();
        classifier.Predict("Blue Bottle Coffee").ShouldBeNull();
    }

    [Fact]
    public void A_familiar_merchant_is_recognised()
    {
        CategoryClassifier classifier = Trained();

        CategoryPrediction? prediction = classifier.Predict("BLUE BOTTLE COFFEE 9999");

        prediction.ShouldNotBeNull();
        prediction.CategoryId.ShouldBe(Coffee);
        prediction.Confidence.ShouldBeGreaterThan(0.65);
    }

    [Fact]
    public void A_merchant_never_seen_before_produces_nothing_confident()
    {
        CategoryClassifier classifier = Trained();

        // No word in this overlaps anything in the training set, so every category is equally
        // (im)plausible and the classifier should decline rather than pick one.
        classifier.Predict("ZZZ QUUX WIDGET").ShouldBeNull();
    }

    [Fact]
    public void Different_merchants_land_in_different_categories()
    {
        CategoryClassifier classifier = Trained();

        classifier.Predict("SHELL OIL 4471")!.CategoryId.ShouldBe(Fuel);
        classifier.Predict("COSTCO WHOLESALE")!.CategoryId.ShouldBe(Groceries);
    }

    [Fact]
    public void A_category_with_too_few_examples_is_not_offered()
    {
        var examples = new List<TrainingExample>();

        for (int i = 0; i < 20; i++)
        {
            examples.Add(new TrainingExample("Blue Bottle Coffee", Coffee));
        }

        // One lonely example of a different category should not become a suggestion.
        examples.Add(new TrainingExample("Rare Merchant Name", Fuel));

        CategoryClassifier classifier = CategoryClassifier.Train(examples);

        classifier.Predict("Rare Merchant Name").ShouldBeNull();
    }

    [Fact]
    public void The_confidence_threshold_is_respected()
    {
        CategoryClassifier classifier = Trained();

        // Asking for near-certainty turns away answers that a lower bar would accept.
        classifier.Predict("BLUE BOTTLE COFFEE", confidenceThreshold: 0.999999).ShouldBeNull();
        classifier.Predict("BLUE BOTTLE COFFEE", confidenceThreshold: 0.1).ShouldNotBeNull();
    }

    [Fact]
    public void Confidence_stays_a_probability()
    {
        CategoryClassifier classifier = Trained();

        CategoryPrediction prediction = classifier.Predict("BLUE BOTTLE COFFEE", 0.0)!;

        prediction.Confidence.ShouldBeGreaterThan(0);
        prediction.Confidence.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void A_long_descriptor_does_not_underflow_to_a_meaningless_score()
    {
        CategoryClassifier classifier = Trained();

        // Scored in logarithms precisely so a descriptor with dozens of words does not
        // multiply out to zero and leave every category looking identical.
        string longText = string.Join(" ", Enumerable.Repeat("blue bottle coffee beans", 40));

        CategoryPrediction? prediction = classifier.Predict(longText);

        prediction.ShouldNotBeNull();
        prediction.CategoryId.ShouldBe(Coffee);
        double.IsNaN(prediction.Confidence).ShouldBeFalse();
    }

    [Fact]
    public void Examples_with_no_usable_words_are_not_counted()
    {
        CategoryClassifier classifier = CategoryClassifier.Train(
        [
            .. Enumerable.Repeat(new TrainingExample("1234 5678", Coffee), 50),
        ]);

        // Every descriptor reduced to nothing, so there is genuinely no training data — and
        // counting them would have inflated one category's prior out of nowhere.
        classifier.TrainingSize.ShouldBe(0);
        classifier.IsUseful.ShouldBeFalse();
    }

    [Fact]
    public void The_reported_support_is_the_number_of_examples_behind_the_answer()
    {
        CategoryClassifier classifier = Trained();

        classifier.Predict("BLUE BOTTLE COFFEE")!.SupportingExamples.ShouldBe(10);
    }

    private static CategoryClassifier Trained()
    {
        var examples = new List<TrainingExample>();

        for (int i = 0; i < 10; i++)
        {
            examples.Add(new TrainingExample($"BLUE BOTTLE COFFEE {1000 + i}", Coffee));
        }

        for (int i = 0; i < 8; i++)
        {
            examples.Add(new TrainingExample($"SHELL OIL {2000 + i}", Fuel));
        }

        for (int i = 0; i < 8; i++)
        {
            examples.Add(new TrainingExample($"COSTCO WHOLESALE {3000 + i}", Groceries));
        }

        return CategoryClassifier.Train(examples);
    }
}

public sealed class CategorySuggesterTests
{
    private const int Coffee = 10;
    private const int Fuel = 20;
    private const int FromFile = 30;
    private const int Remembered = 40;

    [Fact]
    public void A_rule_outranks_everything_else()
    {
        // The rule is the only one of the four the user wrote deliberately, and in this
        // application rather than in whichever program produced the file.
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(payee: "SHELL", fileCategory: FromFile, payeeLast: Remembered),
            [Rule(pattern: "SHELL", categoryId: Fuel)],
            Trained());

        suggestion.Source.ShouldBe(SuggestionSource.Rule);
        suggestion.CategoryId.ShouldBe(Fuel);
        suggestion.Confidence.ShouldBe(1);
        suggestion.Rule.ShouldNotBeNull();
    }

    [Fact]
    public void The_files_own_category_beats_payee_memory()
    {
        // QIF carries the user's own historical decision, which is better evidence than what
        // this application last inferred.
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(payee: "SHELL", fileCategory: FromFile, payeeLast: Remembered),
            [],
            Trained());

        suggestion.Source.ShouldBe(SuggestionSource.FileCategory);
        suggestion.CategoryId.ShouldBe(FromFile);
    }

    [Fact]
    public void Payee_memory_beats_the_statistical_guess()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(payee: "BLUE BOTTLE COFFEE", payeeLast: Remembered),
            [],
            Trained());

        suggestion.Source.ShouldBe(SuggestionSource.PayeeMemory);
        suggestion.CategoryId.ShouldBe(Remembered);
    }

    [Fact]
    public void The_statistical_guess_is_the_last_resort()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(payee: "BLUE BOTTLE COFFEE 9999"),
            [],
            Trained());

        suggestion.Source.ShouldBe(SuggestionSource.Statistical);
        suggestion.CategoryId.ShouldBe(Coffee);
        suggestion.Confidence.ShouldBeLessThan(1);
        suggestion.Describe().ShouldContain("%");
    }

    [Fact]
    public void Nothing_known_suggests_nothing()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(payee: "ZZZ UNKNOWN WIDGET"),
            [],
            Trained());

        suggestion.Source.ShouldBe(SuggestionSource.None);
        suggestion.HasCategory.ShouldBeFalse();
    }

    [Fact]
    public void A_rule_that_only_rewrites_the_payee_still_wins()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(payee: "SQ *BLUE BOTTLE"),
            [Rule(pattern: "SQ *BLUE", categoryId: null, payeeId: 42)],
            Trained());

        suggestion.Source.ShouldBe(SuggestionSource.Rule);
        suggestion.PayeeId.ShouldBe(42);
        suggestion.HasCategory.ShouldBeFalse();
    }

    [Fact]
    public void A_rule_matching_nothing_falls_through()
    {
        CategorySuggestion suggestion = CategorySuggester.Suggest(
            Input(payee: "BLUE BOTTLE COFFEE", payeeLast: Remembered),
            [Rule(pattern: "SHELL", categoryId: Fuel)],
            Trained());

        suggestion.Source.ShouldBe(SuggestionSource.PayeeMemory);
    }

    private static CategorySuggester.SuggestionInput Input(
        string? payee = null,
        string? descriptor = null,
        int accountId = 1,
        decimal amount = -10m,
        int? fileCategory = null,
        int? payeeLast = null) =>
        new(accountId, payee, descriptor ?? payee, Money.FromDecimal(amount), fileCategory, payeeLast);

    private static RuleSpec Rule(string pattern, int? categoryId = 1, int? payeeId = null) =>
        new(1, "Test", 0, RuleMatchField.PayeeOrMemo, RuleMatchKind.Contains, pattern, false,
            null, categoryId, payeeId, true);

    private static CategoryClassifier Trained()
    {
        var examples = new List<TrainingExample>();

        for (int i = 0; i < 10; i++)
        {
            examples.Add(new TrainingExample($"BLUE BOTTLE COFFEE {1000 + i}", Coffee));
        }

        for (int i = 0; i < 10; i++)
        {
            examples.Add(new TrainingExample($"SHELL OIL {2000 + i}", Fuel));
        }

        return CategoryClassifier.Train(examples);
    }
}
