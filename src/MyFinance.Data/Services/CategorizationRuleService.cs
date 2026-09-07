using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Import.Categorization;

namespace MyFinance.Data.Services;

/// <summary>A rule with the names of everything it points at, for the rules screen.</summary>
public sealed record RuleListItem
{
    public required CategorizationRule Rule { get; init; }

    public string? TargetCategoryName { get; init; }

    public string? TargetPayeeName { get; init; }

    public string? AccountName { get; init; }

    public int Id => Rule.Id;

    public string Name => Rule.Name;

    public int Priority => Rule.Priority;

    public bool IsEnabled => Rule.IsEnabled;

    public int TimesApplied => Rule.TimesApplied;

    /// <summary>Plain-English form of the condition, e.g. "Payee contains SHELL".</summary>
    public string Condition
    {
        get
        {
            string fieldName = Rule.MatchField switch
            {
                RuleMatchField.Payee => "Payee",
                RuleMatchField.Memo => "Description",
                RuleMatchField.PayeeOrMemo => "Payee or description",
                RuleMatchField.Amount => "Amount",
                _ => "Something",
            };

            if (Rule.MatchField == RuleMatchField.Amount)
            {
                return $"Amount is {Rule.Pattern}";
            }

            string comparison = Rule.MatchKind switch
            {
                RuleMatchKind.Contains => "contains",
                RuleMatchKind.Equals => "is exactly",
                RuleMatchKind.StartsWith => "starts with",
                RuleMatchKind.EndsWith => "ends with",
                RuleMatchKind.Regex => "matches the expression",
                _ => "matches",
            };

            return $"{fieldName} {comparison} \"{Rule.Pattern}\"";
        }
    }

    /// <summary>Plain-English form of what the rule does when it fires.</summary>
    public string Action
    {
        get
        {
            var parts = new List<string>();

            if (TargetCategoryName is not null)
            {
                parts.Add($"file under {TargetCategoryName}");
            }

            if (TargetPayeeName is not null)
            {
                parts.Add($"rename the payee to {TargetPayeeName}");
            }

            return parts.Count == 0 ? "(does nothing)" : string.Join(", ", parts);
        }
    }

    public string ScopeText => AccountName is null ? "All accounts" : AccountName;
}

/// <summary>The editable shape of a rule.</summary>
public sealed class RuleDraft
{
    public int? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Priority { get; set; }

    public RuleMatchField MatchField { get; set; } = RuleMatchField.PayeeOrMemo;

    public RuleMatchKind MatchKind { get; set; } = RuleMatchKind.Contains;

    public string Pattern { get; set; } = string.Empty;

    public bool IsCaseSensitive { get; set; }

    public int? AccountId { get; set; }

    public int? TargetCategoryId { get; set; }

    public int? TargetPayeeId { get; set; }

    public bool IsEnabled { get; set; } = true;
}

/// <summary>Creates, edits and orders the user's categorization rules.</summary>
public sealed class CategorizationRuleService
{
    public const string NameRequired = "rule.name_required";
    public const string PatternInvalid = "rule.pattern_invalid";
    public const string NoAction = "rule.no_action";
    public const string NotFound = "rule.not_found";

    private readonly IBookContextFactory _factory;

    public CategorizationRuleService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<IReadOnlyList<RuleListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<CategorizationRule> rules = await db.CategorizationRules
            .AsNoTracking()
            .Include(r => r.TargetCategory)
            .ThenInclude(c => c!.Parent)
            .Include(r => r.TargetPayee)
            .Include(r => r.Account)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rules.Select(r => new RuleListItem
            {
                Rule = r,
                TargetCategoryName = r.TargetCategory?.FullName,
                TargetPayeeName = r.TargetPayee?.Name,
                AccountName = r.Account?.Name,
            })
        ];
    }

    /// <summary>The rules in the form the evaluator wants, loaded once per import.</summary>
    public async Task<IReadOnlyList<RuleSpec>> GetSpecsAsync(CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        return await LoadSpecsAsync(db, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(RuleDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Guard(draft);

        await using MyFinanceDbContext db = _factory.CreateContext();

        // New rules go to the end, so adding one never silently changes what an existing
        // rule does by jumping ahead of it.
        int priority = draft.Priority;
        if (priority == 0)
        {
            int highest = await db.CategorizationRules
                .Select(r => (int?)r.Priority)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0;

            priority = highest + 10;
        }

        var rule = new CategorizationRule
        {
            Name = draft.Name.Trim(),
            Priority = priority,
            MatchField = draft.MatchField,
            MatchKind = draft.MatchKind,
            Pattern = draft.Pattern.Trim(),
            IsCaseSensitive = draft.IsCaseSensitive,
            AccountId = draft.AccountId,
            TargetCategoryId = draft.TargetCategoryId,
            TargetPayeeId = draft.TargetPayeeId,
            IsEnabled = draft.IsEnabled,
        };

        db.CategorizationRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return rule.Id;
    }

    public async Task UpdateAsync(RuleDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Guard(draft);

        if (draft.Id is not int id)
        {
            throw new BookValidationException(NotFound, "That rule has not been saved yet.");
        }

        await using MyFinanceDbContext db = _factory.CreateContext();
        CategorizationRule rule = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        rule.Name = draft.Name.Trim();
        rule.Priority = draft.Priority;
        rule.MatchField = draft.MatchField;
        rule.MatchKind = draft.MatchKind;
        rule.Pattern = draft.Pattern.Trim();
        rule.IsCaseSensitive = draft.IsCaseSensitive;
        rule.AccountId = draft.AccountId;
        rule.TargetCategoryId = draft.TargetCategoryId;
        rule.TargetPayeeId = draft.TargetPayeeId;
        rule.IsEnabled = draft.IsEnabled;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        CategorizationRule rule = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        rule.IsEnabled = isEnabled;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        CategorizationRule rule = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        db.CategorizationRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a rule up or down the order.
    /// </summary>
    /// <remarks>
    /// Order is what decides which of two overlapping rules wins, so it has to be directly
    /// adjustable rather than something the user infers from a priority number.
    /// </remarks>
    public async Task MoveAsync(int id, int offset, CancellationToken cancellationToken = default)
    {
        if (offset == 0)
        {
            return;
        }

        await using MyFinanceDbContext db = _factory.CreateContext();

        List<CategorizationRule> rules = await db.CategorizationRules
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int index = rules.FindIndex(r => r.Id == id);
        if (index < 0)
        {
            throw new BookValidationException(NotFound, "That rule no longer exists.");
        }

        int target = Math.Clamp(index + offset, 0, rules.Count - 1);
        if (target == index)
        {
            return;
        }

        CategorizationRule moving = rules[index];
        rules.RemoveAt(index);
        rules.Insert(target, moving);

        // Renumbered in tens so a later insertion has room between two rules.
        for (int i = 0; i < rules.Count; i++)
        {
            rules[i].Priority = (i + 1) * 10;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts how many transactions already in the book a rule would have matched.
    /// </summary>
    /// <remarks>
    /// The way to find out whether a rule says what you meant before letting it loose on an
    /// import. Reads only; nothing is recategorized.
    /// </remarks>
    public async Task<int> CountMatchesAsync(RuleDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (RuleEvaluator.Validate(draft.MatchKind, draft.Pattern) is not null)
        {
            return 0;
        }

        await using MyFinanceDbContext db = _factory.CreateContext();

        var rows = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Payee)
            .Where(t => !t.IsVoid)
            .Select(t => new { t.AccountId, PayeeName = t.Payee!.Name, t.Memo, t.Amount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        RuleSpec spec = ToSpec(draft);

        return rows.Count(r => RuleEvaluator.Matches(
            spec,
            new RuleInput(r.AccountId, r.PayeeName, r.Memo, r.Amount)));
    }

    /// <summary>Records that rules fired, for the diagnostics column on the rules screen.</summary>
    internal static void RecordApplications(
        MyFinanceDbContext db,
        IEnumerable<int> ruleIds,
        DateTimeOffset when)
    {
        Dictionary<int, int> counts = ruleIds
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        if (counts.Count == 0)
        {
            return;
        }

        foreach (CategorizationRule rule in db.CategorizationRules
            .Where(r => counts.Keys.Contains(r.Id)))
        {
            rule.TimesApplied += counts[rule.Id];
            rule.LastAppliedUtc = when;
        }
    }

    internal static async Task<IReadOnlyList<RuleSpec>> LoadSpecsAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken) =>
        await db.CategorizationRules
            .AsNoTracking()
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .Select(r => new RuleSpec(
                r.Id,
                r.Name,
                r.Priority,
                r.MatchField,
                r.MatchKind,
                r.Pattern,
                r.IsCaseSensitive,
                r.AccountId,
                r.TargetCategoryId,
                r.TargetPayeeId,
                r.IsEnabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static RuleSpec ToSpec(RuleDraft draft) =>
        new(
            draft.Id ?? 0,
            draft.Name,
            draft.Priority,
            draft.MatchField,
            draft.MatchKind,
            draft.Pattern,
            draft.IsCaseSensitive,
            draft.AccountId,
            draft.TargetCategoryId,
            draft.TargetPayeeId,
            IsEnabled: true);

    private static void Guard(RuleDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            throw new BookValidationException(NameRequired, "A rule needs a name.");
        }

        if (RuleEvaluator.Validate(draft.MatchKind, draft.Pattern) is string problem)
        {
            throw new BookValidationException(PatternInvalid, problem);
        }

        if (draft.TargetCategoryId is null && draft.TargetPayeeId is null)
        {
            throw new BookValidationException(
                NoAction,
                "A rule has to do something: pick a category to file matches under, a payee to rename them to, or both.");
        }
    }

    private static async Task<CategorizationRule> RequireAsync(
        MyFinanceDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        CategorizationRule? rule = await db.CategorizationRules
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return rule ?? throw new BookValidationException(NotFound, "That rule no longer exists.");
    }
}
