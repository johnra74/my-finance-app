namespace MyFinance.Core.Security;

/// <summary>Coarse strength bands shown while choosing a password.</summary>
public enum PasswordStrengthLevel
{
    TooShort,
    Weak,
    Fair,
    Good,
    Strong,
}

/// <summary>
/// A rough, local password strength estimate for the create-book screen.
/// </summary>
/// <remarks>
/// Deliberately simple and offline. This is guidance for a user choosing a passphrase they
/// can never recover, not a security control — the actual protection is Argon2id's cost.
/// </remarks>
public static class PasswordStrength
{
    public const int MinimumLength = 8;

    public const int RecommendedLength = 12;

    public static PasswordStrengthLevel Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            return PasswordStrengthLevel.TooShort;
        }

        int variety = 0;
        if (password.Any(char.IsLower))
        {
            variety++;
        }

        if (password.Any(char.IsUpper))
        {
            variety++;
        }

        if (password.Any(char.IsDigit))
        {
            variety++;
        }

        if (password.Any(c => !char.IsLetterOrDigit(c)))
        {
            variety++;
        }

        // Length carries more weight than character-class variety, which is where the real
        // entropy in a human-chosen secret comes from.
        int score = (password.Length / 4) + variety;

        return score switch
        {
            <= 3 => PasswordStrengthLevel.Weak,
            4 or 5 => PasswordStrengthLevel.Fair,
            6 or 7 => PasswordStrengthLevel.Good,
            _ => PasswordStrengthLevel.Strong,
        };
    }

    public static string Describe(PasswordStrengthLevel level) => level switch
    {
        PasswordStrengthLevel.TooShort => $"Use at least {MinimumLength} characters",
        PasswordStrengthLevel.Weak => "Weak — consider a longer passphrase",
        PasswordStrengthLevel.Fair => "Fair",
        PasswordStrengthLevel.Good => "Good",
        PasswordStrengthLevel.Strong => "Strong",
        _ => string.Empty,
    };
}
