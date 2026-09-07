using MyFinance.Core.Validation;

namespace MyFinance.Data.Services;

/// <summary>
/// A write was refused because it would break the books.
/// </summary>
/// <remarks>
/// Carries the full <see cref="ValidationResult"/> rather than a single message so the UI can
/// list every problem at once instead of making the user fix them one save at a time.
/// </remarks>
public sealed class BookValidationException : Exception
{
    public BookValidationException(ValidationResult result)
        : base(Describe(result))
    {
        Result = result;
    }

    public BookValidationException(string code, string message)
        : this(ValidationResult.Fail(new ValidationError(code, message)))
    {
    }

    public ValidationResult Result { get; }

    public IReadOnlyList<ValidationError> Errors => Result.Errors;

    private static string Describe(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Errors.Count switch
        {
            0 => "The operation was refused.",
            1 => result.Errors[0].Message,
            _ => string.Join(Environment.NewLine, result.Errors.Select(e => "• " + e.Message)),
        };
    }
}
