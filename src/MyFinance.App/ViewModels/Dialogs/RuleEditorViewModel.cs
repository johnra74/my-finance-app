using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Data.Services;
using MyFinance.Import.Categorization;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>A choice in one of the rule editor's pickers.</summary>
/// <typeparam name="T">The underlying value.</typeparam>
/// <param name="Value">What gets saved.</param>
/// <param name="Text">How it reads on screen.</param>
public readonly record struct Choice<T>(T Value, string Text);

/// <summary>Creates or edits one categorization rule.</summary>
public sealed partial class RuleEditorViewModel : DialogViewModel
{
    private readonly CategorizationRuleService _rules;
    private readonly int? _id;
    private readonly int _priority;

    private RuleEditorViewModel(
        CategorizationRuleService rules,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<PayeeListItem> payees,
        IReadOnlyList<Account> accounts,
        RuleListItem? existing)
    {
        _rules = rules;
        _id = existing?.Id;
        _priority = existing?.Priority ?? 0;

        Categories = categories;
        Payees = payees;
        Accounts = accounts;

        Fields =
        [
            new Choice<RuleMatchField>(RuleMatchField.PayeeOrMemo, "Payee or description"),
            new Choice<RuleMatchField>(RuleMatchField.Payee, "Payee"),
            new Choice<RuleMatchField>(RuleMatchField.Memo, "Description"),
            new Choice<RuleMatchField>(RuleMatchField.Amount, "Amount"),
        ];

        Kinds =
        [
            new Choice<RuleMatchKind>(RuleMatchKind.Contains, "contains"),
            new Choice<RuleMatchKind>(RuleMatchKind.StartsWith, "starts with"),
            new Choice<RuleMatchKind>(RuleMatchKind.EndsWith, "ends with"),
            new Choice<RuleMatchKind>(RuleMatchKind.Equals, "is exactly"),
            new Choice<RuleMatchKind>(RuleMatchKind.Regex, "matches the expression"),
        ];

        _selectedField = Fields[0];
        _selectedKind = Kinds[0];

        if (existing is null)
        {
            return;
        }

        _name = existing.Name;
        _pattern = existing.Rule.Pattern;
        _isCaseSensitive = existing.Rule.IsCaseSensitive;
        _isEnabled = existing.IsEnabled;
        _selectedField = Fields.FirstOrDefault(f => f.Value == existing.Rule.MatchField, Fields[0]);
        _selectedKind = Kinds.FirstOrDefault(k => k.Value == existing.Rule.MatchKind, Kinds[0]);
        _selectedCategory = categories.FirstOrDefault(c => c.Id == existing.Rule.TargetCategoryId);
        _selectedPayee = payees.FirstOrDefault(p => p.Id == existing.Rule.TargetPayeeId);
        _selectedAccount = accounts.FirstOrDefault(a => a.Id == existing.Rule.AccountId);
    }

    public static RuleEditorViewModel ForNew(
        CategorizationRuleService rules,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<PayeeListItem> payees,
        IReadOnlyList<Account> accounts) =>
        new(rules, categories, payees, accounts, null);

    public static RuleEditorViewModel ForExisting(
        CategorizationRuleService rules,
        IReadOnlyList<CategoryListItem> categories,
        IReadOnlyList<PayeeListItem> payees,
        IReadOnlyList<Account> accounts,
        RuleListItem existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return new RuleEditorViewModel(rules, categories, payees, accounts, existing);
    }

    public override string Title => _id is null ? "New rule" : "Edit rule";

    public IReadOnlyList<Choice<RuleMatchField>> Fields { get; }

    public IReadOnlyList<Choice<RuleMatchKind>> Kinds { get; }

    public IReadOnlyList<CategoryListItem> Categories { get; }

    public IReadOnlyList<PayeeListItem> Payees { get; }

    public IReadOnlyList<Account> Accounts { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private Choice<RuleMatchField> _selectedField;

    [ObservableProperty]
    private Choice<RuleMatchKind> _selectedKind;

    [ObservableProperty]
    private string _pattern = string.Empty;

    [ObservableProperty]
    private bool _isCaseSensitive;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private CategoryListItem? _selectedCategory;

    [ObservableProperty]
    private PayeeListItem? _selectedPayee;

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private string _testResult = string.Empty;

    /// <summary>An amount condition is a comparison, so the "contains" picker makes no sense.</summary>
    public bool IsTextCondition => SelectedField.Value != RuleMatchField.Amount;

    public string PatternHint => SelectedField.Value == RuleMatchField.Amount
        ? "A comparison such as \"> 100\", \"< -50\" or a range \"10..50\". Money out is negative."
        : "The text to look for. Case is ignored unless you ask for it.";

    /// <summary>
    /// Counts how many transactions already in the book this rule would have matched.
    /// </summary>
    /// <remarks>
    /// The way to find out whether a rule says what you meant before it runs against an
    /// import. Nothing is recategorized; it only reports.
    /// </remarks>
    [RelayCommand]
    private async Task TestAsync()
    {
        ErrorMessage = null;

        if (RuleEvaluator.Validate(SelectedKind.Value, Pattern) is string problem
            && IsTextCondition)
        {
            ErrorMessage = problem;
            return;
        }

        IsBusy = true;

        try
        {
            int matches = await _rules.CountMatchesAsync(ToDraft()).ConfigureAwait(true);

            TestResult = matches switch
            {
                0 => "This rule matches nothing in your register at the moment.",
                1 => "This rule matches 1 transaction already in your register.",
                _ => $"This rule matches {matches} transactions already in your register.",
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            RuleDraft draft = ToDraft();

            if (_id is null)
            {
                await _rules.CreateAsync(draft).ConfigureAwait(true);
            }
            else
            {
                await _rules.UpdateAsync(draft).ConfigureAwait(true);
            }

            Close(true);
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private RuleDraft ToDraft() => new()
    {
        Id = _id,
        Name = Name,
        Priority = _priority,
        MatchField = SelectedField.Value,
        MatchKind = SelectedKind.Value,
        Pattern = Pattern,
        IsCaseSensitive = IsCaseSensitive,
        AccountId = SelectedAccount?.Id,
        TargetCategoryId = SelectedCategory?.Id,
        TargetPayeeId = SelectedPayee?.Id,
        IsEnabled = IsEnabled,
    };

    partial void OnSelectedFieldChanged(Choice<RuleMatchField> value)
    {
        OnPropertyChanged(nameof(IsTextCondition));
        OnPropertyChanged(nameof(PatternHint));
    }
}
