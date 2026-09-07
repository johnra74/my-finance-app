using MyFinance.Core.Security;

namespace MyFinance.Core.Tests.Security;

public class PasswordStrengthTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public void Anything_below_the_minimum_length_is_too_short(string? password)
    {
        PasswordStrength.Evaluate(password).ShouldBe(PasswordStrengthLevel.TooShort);
    }

    [Fact]
    public void Exactly_the_minimum_length_clears_the_too_short_band()
    {
        string password = new('a', PasswordStrength.MinimumLength);

        PasswordStrength.Evaluate(password).ShouldNotBe(PasswordStrengthLevel.TooShort);
    }

    [Fact]
    public void A_short_single_case_password_rates_weak()
    {
        PasswordStrength.Evaluate("password").ShouldBe(PasswordStrengthLevel.Weak);
    }

    [Fact]
    public void A_long_passphrase_rates_strong_without_needing_symbols()
    {
        // Length is what carries entropy in a human-chosen secret; a long all-lowercase
        // passphrase must not be scored below a short one peppered with punctuation.
        PasswordStrength.Evaluate("correct horse battery staple")
            .ShouldBe(PasswordStrengthLevel.Strong);
    }

    [Fact]
    public void A_long_passphrase_outranks_a_short_complex_password()
    {
        PasswordStrengthLevel passphrase = PasswordStrength.Evaluate("correct horse battery staple");
        PasswordStrengthLevel complex = PasswordStrength.Evaluate("P@ss1!aa");

        ((int)passphrase).ShouldBeGreaterThan((int)complex);
    }

    [Fact]
    public void Adding_character_variety_raises_the_score()
    {
        PasswordStrengthLevel plain = PasswordStrength.Evaluate("aaaaaaaaaaaa");
        PasswordStrengthLevel varied = PasswordStrength.Evaluate("aA1!aaaaaaaa");

        ((int)varied).ShouldBeGreaterThan((int)plain);
    }

    [Fact]
    public void Lengthening_a_password_never_lowers_its_rating()
    {
        PasswordStrengthLevel previous = PasswordStrengthLevel.TooShort;

        for (int length = PasswordStrength.MinimumLength; length <= 40; length++)
        {
            PasswordStrengthLevel current = PasswordStrength.Evaluate(new string('a', length));
            ((int)current).ShouldBeGreaterThanOrEqualTo((int)previous);
            previous = current;
        }
    }

    [Theory]
    [InlineData(PasswordStrengthLevel.TooShort)]
    [InlineData(PasswordStrengthLevel.Weak)]
    [InlineData(PasswordStrengthLevel.Fair)]
    [InlineData(PasswordStrengthLevel.Good)]
    [InlineData(PasswordStrengthLevel.Strong)]
    public void Every_level_has_something_to_show_the_user(PasswordStrengthLevel level)
    {
        PasswordStrength.Describe(level).ShouldNotBeNullOrWhiteSpace();
    }
}
