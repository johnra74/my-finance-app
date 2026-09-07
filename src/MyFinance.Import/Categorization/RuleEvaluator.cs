using System.Globalization;
using System.Text.RegularExpressions;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Import.Categorization;

/// <summary>A categorization rule, in the form the evaluator needs and free of the database.</summary>
/// <param name="Id">The rule's database id, so a match can be reported back.</param>
/// <param name="Name">What the user called it.</param>
/// <param name="Priority">Lower runs first.</param>
/// <param name="Field">Which part of the transaction to look at.</param>
/// <param name="Kind">How to compare.</param>
/// <param name="Pattern">What to compare against.</param>
/// <param name="IsCaseSensitive">Whether case matters. Almost never wanted.</param>
/// <param name="AccountId">Restricts the rule to one account when set.</param>
/// <param name="TargetCategoryId">The category to assign.</param>
/// <param name="TargetPayeeId">A payee to rewrite to, when the rule also tidies the name.</param>
/// <param name="IsEnabled">Disabled rules are kept but skipped.</param>
public sealed record RuleSpec(
    int Id,
    string Name,
    int Priority,
    RuleMatchField Field,
    RuleMatchKind Kind,
    string Pattern,
    bool IsCaseSensitive,
    int? AccountId,
    int? TargetCategoryId,
    int? TargetPayeeId,
    bool IsEnabled);

/// <summary>The transaction being tested against the rules.</summary>
/// <param name="AccountId">Which account it is going into.</param>
/// <param name="PayeeName">The payee as it will be recorded.</param>
/// <param name="Memo">The raw descriptor.</param>
/// <param name="Amount">Signed amount.</param>
public readonly record struct RuleInput(int AccountId, string? PayeeName, string? Memo, Money Amount);

/// <summary>Which rule fired and what it says to do.</summary>
/// <param name="Rule">The rule that matched.</param>
public sealed record RuleMatch(RuleSpec Rule)
{
    public int? CategoryId => Rule.TargetCategoryId;

    public int? PayeeId => Rule.TargetPayeeId;
}

/// <summary>
/// Applies the user's own categorization rules to a transaction.
/// </summary>
/// <remarks>
/// <para>
/// Rules run before anything inferred, because they are the one part of categorization the
/// user wrote deliberately. First match wins, in priority order, so a specific rule placed
/// above a general one overrides it — which is the behaviour anyone who has used a mail
/// filter already expects.
/// </para>
/// <para>
/// A rule that throws is treated as not matching rather than being allowed to abort an
/// import: a bad regular expression is a mistake in one rule, not a reason to refuse a
/// four-hundred-row statement.
/// </para>
/// </remarks>
public static class RuleEvaluator
{
    /// <summary>
    /// How long a single regular expression may run before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Users write rules by hand, and a pattern with nested quantifiers can take exponential
    /// time on an unlucky descriptor. A timeout turns that from a frozen application into one
    /// rule quietly not matching.
    /// </remarks>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Rules in the order they are evaluated: by priority, then by age.</summary>
    public static IEnumerable<RuleSpec> InEvaluationOrder(IEnumerable<RuleSpec> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id);
    }

    /// <summary>The first rule that matches, or null when none does.</summary>
    public static RuleMatch? FirstMatch(IEnumerable<RuleSpec> rules, RuleInput input)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (RuleSpec rule in InEvaluationOrder(rules))
        {
            if (Matches(rule, input))
            {
                return new RuleMatch(rule);
            }
        }

        return null;
    }

    /// <summary>Whether one rule matches one transaction.</summary>
    public static bool Matches(RuleSpec rule, RuleInput input)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (!rule.IsEnabled)
        {
            return false;
        }

        if (rule.AccountId is int account && account != input.AccountId)
        {
            return false;
        }

        if (rule.Field == RuleMatchField.Amount)
        {
            return MatchesAmount(rule.Pattern, input.Amount);
        }

        return rule.Field switch
        {
            RuleMatchField.Payee => MatchesText(rule, input.PayeeName),
            RuleMatchField.Memo => MatchesText(rule, input.Memo),
            RuleMatchField.PayeeOrMemo =>
                MatchesText(rule, input.PayeeName) || MatchesText(rule, input.Memo),
            _ => false,
        };
    }

    /// <summary>
    /// Checks that a pattern is usable before it is saved.
    /// </summary>
    /// <returns>Null when the pattern is fine, or an explanation of what is wrong with it.</returns>
    public static string? Validate(RuleMatchKind kind, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "A rule needs something to match against.";
        }

        if (kind == RuleMatchKind.Regex)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.None, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                return $"That is not a valid regular expression: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// Checks an amount condition, which is written as a comparison rather than a pattern.
    /// </summary>
    /// <remarks>
    /// Accepts <c>&gt; 100</c>, <c>&lt;= -50</c>, <c>= 18.40</c>, and a range as
    /// <c>10..50</c>. Comparisons are against the signed amount, so <c>&lt; -100</c> means
    /// "more than a hundred going out" — which reads oddly the first time and is the only
    /// consistent choice given money out is negative everywhere else in the application.
    /// </remarks>
    public static bool MatchesAmount(string? pattern, Money amount)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        string text = pattern.Trim();

        int range = text.IndexOf("..", StringComparison.Ordinal);
        if (range > 0)
        {
            return TryParseAmount(text[..range], out Money low)
                && TryParseAmount(text[(range + 2)..], out Money high)
                && amount >= Money.FromMinorUnits(Math.Min(low.MinorUnits, high.MinorUnits))
                && amount <= Money.FromMinorUnits(Math.Max(low.MinorUnits, high.MinorUnits));
        }

        string comparison = text switch
        {
            _ when text.StartsWith(">=", StringComparison.Ordinal) => ">=",
            _ when text.StartsWith("<=", StringComparison.Ordinal) => "<=",
            _ when text.StartsWith("<>", StringComparison.Ordinal) => "<>",
            _ when text.StartsWith('>') => ">",
            _ when text.StartsWith('<') => "<",
            _ when text.StartsWith('=') => "=",
            _ => "=",
        };

        string valueText = text[comparison.Length..];
        if (comparison == "=" && !text.StartsWith('='))
        {
            valueText = text;
        }

        if (!TryParseAmount(valueText, out Money threshold))
        {
            return false;
        }

        return comparison switch
        {
            ">" => amount > threshold,
            ">=" => amount >= threshold,
            "<" => amount < threshold,
            "<=" => amount <= threshold,
            "<>" => amount != threshold,
            _ => amount == threshold,
        };
    }

    private static bool TryParseAmount(string text, out Money amount)
    {
        if (Money.TryParse(text, CultureInfo.CurrentCulture, out amount))
        {
            return true;
        }

        return Money.TryParse(text, CultureInfo.InvariantCulture, out amount);
    }

    private static bool MatchesText(RuleSpec rule, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        StringComparison comparison = rule.IsCaseSensitive
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;

        try
        {
            return rule.Kind switch
            {
                RuleMatchKind.Contains => value.Contains(rule.Pattern, comparison),
                RuleMatchKind.Equals => value.Equals(rule.Pattern, comparison),
                RuleMatchKind.StartsWith => value.StartsWith(rule.Pattern, comparison),
                RuleMatchKind.EndsWith => value.EndsWith(rule.Pattern, comparison),
                RuleMatchKind.Regex => Regex.IsMatch(
                    value,
                    rule.Pattern,
                    rule.IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase,
                    RegexTimeout),
                _ => false,
            };
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological pattern must not hold up the import.
            return false;
        }
        catch (ArgumentException)
        {
            // An invalid expression that was saved before validation existed, or edited in
            // the database by hand. Treated as not matching rather than fatal.
            return false;
        }
    }
}
