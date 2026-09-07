using MyFinance.Core.Payees;

namespace MyFinance.Core.Tests.Payees;

public sealed class PayeeNormalizerTests
{
    [Theory]
    [InlineData("Blue Bottle Coffee", "BLUE BOTTLE COFFEE")]
    [InlineData("blue   bottle  coffee", "BLUE BOTTLE COFFEE")]
    [InlineData("  Blue Bottle Coffee.  ", "BLUE BOTTLE COFFEE")]
    [InlineData("AT&T", "AT T")]
    [InlineData("Mr. Cooper", "MR COOPER")]
    [InlineData("SQ *BLUE BOTTLE 1234", "SQ BLUE BOTTLE 1234")]
    public void Case_punctuation_and_spacing_all_collapse(string input, string expected) =>
        PayeeNormalizer.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Nothing_normalizes_to_an_empty_string(string? input) =>
        PayeeNormalizer.Normalize(input).ShouldBe(string.Empty);

    [Fact]
    public void Trailing_punctuation_does_not_leave_a_dangling_space()
    {
        // The separator is only emitted before the next real character, so a name ending in
        // punctuation does not normalize to something with a trailing space that then fails
        // to match the same name typed without it.
        PayeeNormalizer.Normalize("Costco!!!").ShouldBe("COSTCO");
        PayeeNormalizer.Normalize("Costco").ShouldBe(PayeeNormalizer.Normalize("Costco!!!"));
    }

    [Fact]
    public void Equivalent_names_are_recognised_as_the_same_payee()
    {
        PayeeNormalizer.AreEquivalent("Woodgrove Mortgage", "woodgrove   mortgage!").ShouldBeTrue();
        PayeeNormalizer.AreEquivalent("Shell", "Chevron").ShouldBeFalse();
        PayeeNormalizer.AreEquivalent(null, "").ShouldBeTrue();
    }

    [Fact]
    public void Digits_are_kept_because_they_distinguish_real_payees()
    {
        PayeeNormalizer.Normalize("Shell 4471").ShouldBe("SHELL 4471");
        PayeeNormalizer.AreEquivalent("Shell 4471", "Shell 9902").ShouldBeFalse();
    }
}
