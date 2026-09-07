using MyFinance.Core.Enums;

namespace MyFinance.Core.Entities;

/// <summary>
/// A user-authored rule that assigns a category to matching transactions on import.
/// Rules are deterministic and inspectable; they run ahead of payee memory and ahead of the
/// statistical suggester, so the user can always override the machine.
/// </summary>
public class CategorizationRule
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Lower runs first. Ties break on <see cref="Id"/>.</summary>
    public int Priority { get; set; }

    public RuleMatchField MatchField { get; set; }

    public RuleMatchKind MatchKind { get; set; }

    public required string Pattern { get; set; }

    public bool IsCaseSensitive { get; set; }

    /// <summary>Restricts the rule to one account when set.</summary>
    public int? AccountId { get; set; }

    public Account? Account { get; set; }

    public int? TargetCategoryId { get; set; }

    public Category? TargetCategory { get; set; }

    /// <summary>Rewrites the payee to a canonical name when the rule fires.</summary>
    public int? TargetPayeeId { get; set; }

    public Payee? TargetPayee { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Diagnostics for the rules screen: how often this rule actually fires.</summary>
    public int TimesApplied { get; set; }

    public DateTimeOffset? LastAppliedUtc { get; set; }
}
