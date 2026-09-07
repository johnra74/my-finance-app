using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Import.Categorization;
using MyFinance.Import.Dedupe;
using MyFinance.Import;
using MyFinance.Import.Model;
using MyFinance.Import.Ofx;
using MyFinance.Import.Qif;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>Which part of the import the user is looking at.</summary>
public enum ImportStep
{
    ChooseFile = 0,
    ChooseAccount = 1,
    Review = 2,
    Done = 3,
}

/// <summary>One row of the preview grid.</summary>
public sealed partial class ImportRowViewModel : ObservableObject
{
    private readonly ImportWizardViewModel _owner;

    public ImportRowViewModel(
        ImportWizardViewModel owner,
        ImportCandidate candidate,
        IReadOnlyList<CategoryListItem> categories)
    {
        _owner = owner;
        Candidate = candidate;
        Categories = categories;

        _include = candidate.IncludedByDefault;
        _payeeName = candidate.Payee.Name;
        _category = categories.FirstOrDefault(c => c.Id == candidate.SuggestedCategoryId);
    }

    public ImportCandidate Candidate { get; }

    /// <summary>
    /// The whole category list, shared by every row rather than copied per row — five hundred
    /// private copies of the tree is what makes a grid like this take seconds to open.
    /// </summary>
    public IReadOnlyList<CategoryListItem> Categories { get; }

    public int Index => Candidate.Index;

    public DateOnly Date => Candidate.Date;

    public string Descriptor => Candidate.RawDescriptor;

    public bool CanBeIncluded => Candidate.CanBeIncluded;

    [ObservableProperty]
    private bool _include;

    [ObservableProperty]
    private string _payeeName;

    [ObservableProperty]
    private CategoryListItem? _category;

    /// <summary>The amount as it will actually be recorded, after any sign correction.</summary>
    public Money Amount => _owner.ReverseSigns
        ? Candidate.StatedAmount.Negated()
        : Candidate.StatedAmount;

    public string AmountText => Amount.ToAccountingString(CultureInfo.CurrentCulture);

    /// <summary>Short label describing what will happen to this row.</summary>
    public string Status => Candidate.Duplicate.Kind switch
    {
        DuplicateKind.ExternalId => "Already imported",
        DuplicateKind.RepeatedInFile => "Repeated in file",
        DuplicateKind.Likely => "Likely duplicate",
        DuplicateKind.Possible => "Possible duplicate",
        _ when Candidate.Payee.IsNew => "New payee",
        _ => "New",
    };

    public string? StatusDetail => Candidate.Duplicate.Explanation;

    /// <summary>Where the proposed category came from, e.g. "Rule: Shell" or "Suggested (82%)".</summary>
    public string CategorySource => Candidate.SuggestionSourceText;

    /// <summary>
    /// True when the category was inferred statistically rather than decided by the user.
    /// Marked in the grid so a guess is never mistaken for a certainty.
    /// </summary>
    public bool IsGuess => Candidate.IsGuess;

    public bool IsDuplicate => Candidate.Duplicate.Kind != DuplicateKind.None;

    public bool IsNewPayee => Candidate.Payee.IsNew;

    internal void NotifyAmountChanged()
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(AmountText));
    }

    partial void OnIncludeChanged(bool value) => _owner.Retally();
}

/// <summary>
/// Walks the user from a downloaded file to transactions in a register.
/// </summary>
/// <remarks>
/// One window with four steps rather than four windows, so going back does not lose the
/// work already done. Nothing is written until the last step, which makes cancelling free at
/// any point.
/// </remarks>
public sealed partial class ImportWizardViewModel : DialogViewModel
{
    private readonly ImportService _import;
    private readonly AccountService _accounts;
    private readonly CategoryService _categories;
    private readonly IDialogService _dialogs;
    private readonly IModalService _modals;

    private ImportedFile? _parsed;
    private ImportPreview? _preview;
    private IReadOnlyList<CategoryListItem> _categoryList = [];

    public ImportWizardViewModel(
        ImportService import,
        AccountService accounts,
        CategoryService categories,
        IDialogService dialogs,
        IModalService modals)
    {
        _import = import;
        _accounts = accounts;
        _categories = categories;
        _dialogs = dialogs;
        _modals = modals;
    }

    public override string Title => "Import a statement";

    public ObservableCollection<Account> Accounts { get; } = [];

    public ObservableCollection<ImportRowViewModel> Rows { get; } = [];

    public ObservableCollection<string> Diagnostics { get; } = [];

    /// <summary>Pre-selects an account, used when starting from that account's register.</summary>
    public int? FixedAccountId { get; set; }

    [ObservableProperty]
    private ImportStep _step = ImportStep.ChooseFile;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private bool _reverseSigns;

    [ObservableProperty]
    private bool _createMissingCategories;

    [ObservableProperty]
    private string _missingCategoriesMessage = string.Empty;

    [ObservableProperty]
    private bool _hasMissingCategories;

    [ObservableProperty]
    private string _statementSummary = string.Empty;

    [ObservableProperty]
    private string _signMessage = string.Empty;

    [ObservableProperty]
    private bool _showSignWarning;

    [ObservableProperty]
    private string _balanceMessage = string.Empty;

    [ObservableProperty]
    private bool _balanceAgrees;

    [ObservableProperty]
    private string _countsMessage = string.Empty;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    /// <summary>Set once the import has been written, so the caller can refresh and navigate.</summary>
    public ImportSummary? Result { get; private set; }

    public int? ImportedAccountId { get; private set; }

    public bool IsChoosingFile => Step == ImportStep.ChooseFile;

    public bool IsChoosingAccount => Step == ImportStep.ChooseAccount;

    public bool IsReviewing => Step == ImportStep.Review;

    public bool IsDone => Step == ImportStep.Done;

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public bool CanGoBack => Step is ImportStep.ChooseAccount or ImportStep.Review;

    /// <summary>Opens the file picker and parses whatever is chosen.</summary>
    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        string? path = _dialogs.PickStatementFile();
        if (path is null)
        {
            return;
        }

        await LoadFileAsync(path).ConfigureAwait(true);
    }

    /// <summary>Parses a statement file and moves to account selection if it is usable.</summary>
    public async Task LoadFileAsync(string path)
    {
        ErrorMessage = null;

        try
        {
            FilePath = path;
            FileName = Path.GetFileName(path);

            ImportedFile? parsed = await RunBusyAsync(
                $"Reading {FileName}",
                (_, _) => Task.FromResult(StatementFileReader.Read(path)))
                .ConfigureAwait(true);

            if (parsed is null)
            {
                return;
            }

            _parsed = parsed;

            Diagnostics.Clear();
            foreach (ImportDiagnostic diagnostic in parsed.Diagnostics.Where(d => d.Severity != ImportSeverity.Info))
            {
                Diagnostics.Add(diagnostic.Message);
            }

            OnPropertyChanged(nameof(HasDiagnostics));

            if (parsed.HasBlockingError)
            {
                // The bank's own words, not a paraphrase: "your credentials have expired" is
                // far more useful than anything this application could infer.
                ErrorMessage = string.Join(
                    Environment.NewLine,
                    parsed.Errors.Select(e => e.Message));
                return;
            }

            ImportedStatement statement = parsed.Statements[0];
            StatementSummary = DescribeStatement(statement);

            await LoadAccountsAsync(statement).ConfigureAwait(true);
            Step = ImportStep.ChooseAccount;
            RefreshStepFlags();
        }
        catch (OfxParseException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (QifParseException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (IOException ex)
        {
            ErrorMessage = $"That file could not be read: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateAccountAsync()
    {
        if (_parsed is null)
        {
            return;
        }

        ImportedStatement statement = _parsed.Statements[0];

        AccountEditorViewModel editor = AccountEditorViewModel.ForNewFromStatement(
            _accounts,
            suggestedName: SuggestAccountName(statement),
            institution: statement.Institution,
            type: statement.Account.MappedType,
            maskedNumber: OfxAccountKey.Mask(statement.Account.AccountId),
            currencyCode: statement.CurrencyCode);

        if (!_modals.Show(editor) || editor.SavedAccountId is not int created)
        {
            return;
        }

        await LoadAccountsAsync(statement).ConfigureAwait(true);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == created);
    }

    [RelayCommand]
    private async Task ContinueToReviewAsync()
    {
        if (_parsed is null || SelectedAccount is null)
        {
            ErrorMessage = "Choose which account this statement belongs to.";
            return;
        }

        ErrorMessage = null;

        try
        {
            ImportedStatement statement = _parsed.Statements[0];

            _categoryList = await _categories.GetAllAsync().ConfigureAwait(true);

            int accountId = SelectedAccount.Id;
            IReadOnlyList<ImportDiagnostic> parsedDiagnostics = _parsed.Diagnostics;

            ImportPreview? preview = await RunBusyAsync(
                "Reading the statement",
                (report, token) => _import.PrepareAsync(
                    statement, accountId, parsedDiagnostics, report, token))
                .ConfigureAwait(true);

            if (preview is null)
            {
                return;
            }

            _preview = preview;
            ReverseSigns = preview.Sign.PreselectInversion;

            Rows.Clear();
            foreach (ImportCandidate candidate in preview.Candidates)
            {
                Rows.Add(new ImportRowViewModel(this, candidate, _categoryList));
            }

            ShowSignWarning = preview.Sign.Confidence != SignConfidence.Unknown
                || preview.Sign.Suggested == SignPolarity.Inverted;
            SignMessage = preview.Sign.Explanation;

            // A QIF export carries the user's own filing from whichever program produced it.
            // Offering to bring the categories across is the difference between a ledger
            // arriving intact and every row landing blank.
            IReadOnlyList<string> missing = preview.MissingCategoryPaths;
            HasMissingCategories = missing.Count > 0;

            MissingCategoriesMessage = missing.Count switch
            {
                0 => string.Empty,
                1 => $"This file uses one category your book does not have: {missing[0]}.",
                _ => $"This file uses {missing.Count} categories your book does not have, including {missing[0]}.",
            };

            Step = ImportStep.Review;
            RefreshStepFlags();
            Retally();
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_preview is null || SelectedAccount is null)
        {
            return;
        }

        ErrorMessage = null;

        try
        {
            var request = new ImportRequest
            {
                AccountId = SelectedAccount.Id,
                SourceFileName = FileName,
                ReverseSigns = ReverseSigns,
                CreateMissingCategories = CreateMissingCategories,
                Rows =
                [
                    .. Rows.Select(r => new ImportRowDecision
                    {
                        Index = r.Index,
                        Include = r.Include,
                        PayeeName = r.PayeeName,
                        CategoryId = r.Category?.Id,
                    })
                ],
            };

            ImportPreview preview = _preview;

            ImportSummary? summary = await RunBusyAsync(
                "Writing the transactions",
                (report, token) => _import.CommitAsync(preview, request, report, token))
                .ConfigureAwait(true);

            if (summary is null)
            {
                // The write runs inside a transaction, so stopping leaves nothing behind.
                ErrorMessage = WasCancelled
                    ? "Stopped. Nothing was imported — the account is exactly as it was."
                    : null;
                return;
            }

            // Recorded only once the import has actually been accepted, so backing out of the
            // review really does leave the book untouched, as the first screen promises.
            // From here on, statements from this bank select this account by themselves.
            await _import
                .LinkAccountAsync(SelectedAccount.Id, _preview.Statement.Account)
                .ConfigureAwait(true);

            Result = summary;
            ImportedAccountId = SelectedAccount.Id;
            ResultMessage = DescribeResult(summary);

            Step = ImportStep.Done;
            RefreshStepFlags();
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        ErrorMessage = null;

        Step = Step switch
        {
            ImportStep.Review => ImportStep.ChooseAccount,
            ImportStep.ChooseAccount => ImportStep.ChooseFile,
            _ => Step,
        };

        RefreshStepFlags();
    }

    [RelayCommand]
    private void Finish() => Close(Result is not null);

    /// <summary>
    /// Recomputes the counts and the balance the import would produce.
    /// </summary>
    /// <remarks>
    /// The live comparison against the statement's own closing balance is the real safety
    /// net here: whatever the sign heuristic concluded, the user can see for themselves
    /// whether the result agrees with their bank before committing anything.
    /// </remarks>
    internal void Retally()
    {
        if (_preview is null)
        {
            return;
        }

        List<ImportRowViewModel> included = [.. Rows.Where(r => r.Include && r.CanBeIncluded)];

        int newPayees = included
            .Where(r => r.Candidate.Payee.IsNew)
            .Select(r => r.PayeeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int guesses = included.Count(r => r.IsGuess);

        CountsMessage =
            $"{included.Count} of {Rows.Count} selected · {Rows.Count - included.Count} skipped · {newPayees} new payee{(newPayees == 1 ? string.Empty : "s")}"
            + (guesses > 0 ? $" · {guesses} category guess{(guesses == 1 ? string.Empty : "es")} to check" : string.Empty);

        Money projected = _preview.CurrentBalance + Money.Sum(included.Select(r => r.Amount));

        if (_preview.Statement.LedgerBalance is ImportedBalance ledger)
        {
            BalanceAgrees = projected == ledger.Amount;

            BalanceMessage = BalanceAgrees
                ? $"Balance after importing: {projected.ToAccountingString(CultureInfo.CurrentCulture)} — matches the statement"
                : $"Balance after importing: {projected.ToAccountingString(CultureInfo.CurrentCulture)} — the statement says {ledger.Amount.ToAccountingString(CultureInfo.CurrentCulture)}";
        }
        else
        {
            BalanceAgrees = true;
            BalanceMessage =
                $"Balance after importing: {projected.ToAccountingString(CultureInfo.CurrentCulture)}";
        }
    }

    private void SetAll(bool include)
    {
        foreach (ImportRowViewModel row in Rows.Where(r => r.CanBeIncluded))
        {
            row.Include = include;
        }

        Retally();
    }

    private async Task LoadAccountsAsync(ImportedStatement statement)
    {
        IReadOnlyList<Account> accounts = await _accounts.GetAllAsync().ConfigureAwait(true);

        Accounts.Clear();
        foreach (Account account in accounts)
        {
            Accounts.Add(account);
        }

        int? preferred = FixedAccountId
            ?? await _import.MatchAccountAsync(statement.Account).ConfigureAwait(true);

        SelectedAccount = preferred is int id
            ? Accounts.FirstOrDefault(a => a.Id == id)
            : null;
    }

    private static string DescribeStatement(ImportedStatement statement)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(statement.Institution))
        {
            parts.Add(statement.Institution);
        }

        // Only ever the masked form: nothing in this application needs the real number, and
        // showing it on screen would be the one place it leaked.
        if (OfxAccountKey.Mask(statement.Account.AccountId) is string masked)
        {
            parts.Add(masked);
        }

        parts.Add($"{statement.Transactions.Count} transaction{(statement.Transactions.Count == 1 ? string.Empty : "s")}");

        if (statement.FirstPosted is DateOnly from && statement.LastPosted is DateOnly to)
        {
            parts.Add($"{from:d MMM yyyy} to {to:d MMM yyyy}");
        }

        if (statement.LedgerBalance is ImportedBalance balance)
        {
            parts.Add($"closing balance {balance.Amount.ToAccountingString(CultureInfo.CurrentCulture)}");
        }

        return string.Join(" · ", parts);
    }

    private static string SuggestAccountName(ImportedStatement statement)
    {
        string institution = statement.Institution ?? "Imported";
        string kind = statement.Account.MappedType?.ToString() ?? statement.Account.Kind.ToString();

        return $"{institution} {kind}".Trim();
    }

    private string DescribeResult(ImportSummary summary)
    {
        var parts = new List<string>
        {
            $"Added {summary.Added} transaction{(summary.Added == 1 ? string.Empty : "s")}.",
        };

        if (summary.Skipped > 0)
        {
            parts.Add($"Skipped {summary.Skipped} already in the register or not selected.");
        }

        if (summary.PayeesCreated > 0)
        {
            parts.Add($"Created {summary.PayeesCreated} payee{(summary.PayeesCreated == 1 ? string.Empty : "s")}.");
        }

        if (summary.CategoriesCreated > 0)
        {
            parts.Add($"Created {summary.CategoriesCreated} categor{(summary.CategoriesCreated == 1 ? "y" : "ies")}.");
        }

        if (summary.RuleMatches > 0)
        {
            parts.Add($"{summary.RuleMatches} row{(summary.RuleMatches == 1 ? " was" : "s were")} categorized by your rules.");
        }

        parts.Add(summary.AgreesWithStatement
            ? $"The balance is now {summary.BalanceAfter.ToAccountingString(CultureInfo.CurrentCulture)}, which matches the statement."
            : $"The balance is now {summary.BalanceAfter.ToAccountingString(CultureInfo.CurrentCulture)}.");

        return string.Join(" ", parts);
    }

    private void RefreshStepFlags()
    {
        OnPropertyChanged(nameof(IsChoosingFile));
        OnPropertyChanged(nameof(IsChoosingAccount));
        OnPropertyChanged(nameof(IsReviewing));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(HasDiagnostics));
    }

    partial void OnReverseSignsChanged(bool value)
    {
        foreach (ImportRowViewModel row in Rows)
        {
            row.NotifyAmountChanged();
        }

        Retally();
    }

    partial void OnStepChanged(ImportStep value) => RefreshStepFlags();
}
