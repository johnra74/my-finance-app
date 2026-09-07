using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Security;
using MyFinance.Data.Services;
using MyFinance.Import.Model;
using MyFinance.Import.Ofx;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// A real encrypted book on disk with the services wired over it.
/// </summary>
/// <remarks>
/// Deliberately not an in-memory or SQLite-in-memory provider. The behaviour under test is
/// mostly relational — cascade deletes, the self-referencing transfer key, unique indexes,
/// and aggregates written as SQL — and a provider that fakes those would pass while the
/// real thing failed.
/// </remarks>
internal sealed class BookHarness : IDisposable
{
    private readonly TempBook _temp = new();
    private readonly Book _book;

    public BookHarness()
    {
        _book = _temp.Create();

        Accounts = new AccountService(_book);
        Categories = new CategoryService(_book);
        Payees = new PayeeService(_book);
        Register = new RegisterService(_book);
        Reconcile = new ReconcileService(_book);
        Import = new ImportService(_book);
        Settings = new SettingsService(_book);
        Rules = new CategorizationRuleService(_book);
        Schedules = new ScheduleService(_book);
        Reports = new ReportService(_book);
        Budgets = new BudgetService(_book);
        Migration = new MigrationService(_book);
        Suggestions = new SuggestionService(_book);
        Export = new BookExportService(_book);
        Investments = new InvestmentService(_book, Register);
    }

    public AccountService Accounts { get; }

    public CategoryService Categories { get; }

    public PayeeService Payees { get; }

    public RegisterService Register { get; }

    public ReconcileService Reconcile { get; }

    public ImportService Import { get; }

    public SettingsService Settings { get; }

    public CategorizationRuleService Rules { get; }

    public ScheduleService Schedules { get; }

    public ReportService Reports { get; }

    public BudgetService Budgets { get; }

    public MigrationService Migration { get; }

    public SuggestionService Suggestions { get; }

    public BookExportService Export { get; }

    public InvestmentService Investments { get; }

    public MyFinanceDbContext CreateContext() => _book.CreateContext();

    /// <summary>Where the book lives, for tests that care about the path itself.</summary>
    public string DatabasePath => _book.DatabasePath;

    /// <summary>Creates an account and returns its id.</summary>
    public Task<int> AddAccountAsync(
        string name,
        AccountType type = AccountType.Checking,
        decimal openingBalance = 0m) =>
        Accounts.CreateAsync(new AccountDraft
        {
            Name = name,
            Type = type,
            OpeningBalance = Money.FromDecimal(openingBalance),
        });

    /// <summary>Records an ordinary transaction and returns its id.</summary>
    public Task<int> AddTransactionAsync(
        int accountId,
        decimal amount,
        DateOnly? date = null,
        string? payee = null,
        int? categoryId = null,
        ClearedStatus cleared = ClearedStatus.Uncleared)
    {
        var draft = new TransactionDraft
        {
            AccountId = accountId,
            Date = date ?? new DateOnly(2026, 1, 15),
            Amount = Money.FromDecimal(amount),
            PayeeName = payee,
            ClearedStatus = cleared,
            Splits = categoryId is null
                ? []
                : [new SplitDraft { CategoryId = categoryId, Amount = Money.FromDecimal(amount) }],
        };

        return Register.SaveAsync(draft);
    }

    /// <summary>Finds a seeded category by its display path, e.g. "Food : Groceries".</summary>
    public async Task<int> CategoryIdAsync(string fullName)
    {
        IReadOnlyList<CategoryListItem> all = await Categories.GetAllAsync();

        CategoryListItem match = all.FirstOrDefault(c =>
            string.Equals(c.FullName, fullName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No seeded category called '{fullName}'.");

        return match.Id;
    }

    /// <summary>Schedules a monthly bill and returns its id.</summary>
    public Task<int> AddBillAsync(
        int accountId,
        string payee,
        decimal amount,
        DateOnly startDate,
        MyFinance.Core.Enums.RecurrenceFrequency frequency =
            MyFinance.Core.Enums.RecurrenceFrequency.Monthly) =>
        Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = accountId,
            PayeeName = payee,
            Amount = Money.FromDecimal(amount),
            Frequency = frequency,
            StartDate = startDate,
        });

    /// <summary>Creates a categorization rule and returns its id.</summary>
    public Task<int> AddRuleAsync(
        string name,
        string pattern,
        int? categoryId,
        MyFinance.Core.Enums.RuleMatchField field = MyFinance.Core.Enums.RuleMatchField.PayeeOrMemo,
        int? accountId = null) =>
        Rules.CreateAsync(new RuleDraft
        {
            Name = name,
            Pattern = pattern,
            MatchField = field,
            TargetCategoryId = categoryId,
            AccountId = accountId,
        });

    /// <summary>Parses QIF text the way the wizard would, and returns the one statement.</summary>
    public ImportedStatement ParseQif(string qifText)
    {
        ImportedFile result = MyFinance.Import.Qif.QifParser.Parse(qifText);

        if (result.HasBlockingError)
        {
            throw new InvalidOperationException(
                "Fixture did not parse: " + string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        return result.Statements.Single();
    }

    /// <summary>Plans and commits a QIF import, accepting every default.</summary>
    public async Task<ImportSummary> ImportQifAsync(
        int accountId,
        string qifText,
        bool createMissingCategories = false)
    {
        ImportPreview preview = await Import.PrepareAsync(ParseQif(qifText), accountId);

        return await Import.CommitAsync(preview, new ImportRequest
        {
            AccountId = accountId,
            CreateMissingCategories = createMissingCategories,
            Rows = [],
        });
    }

    /// <summary>Parses statement text the way the wizard would, and returns the one statement.</summary>
    public ImportedStatement ParseStatement(string ofxText)
    {
        ImportedFile result = OfxStatementMapper.ToImported(
            OfxStatementReader.Read(OfxParser.Parse(ofxText)));

        if (result.HasBlockingError)
        {
            throw new InvalidOperationException(
                "Fixture did not parse: " + string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        return result.Statements.Single();
    }

    /// <summary>Plans an import of statement text into an account.</summary>
    public Task<ImportPreview> PrepareAsync(int accountId, string ofxText) =>
        Import.PrepareAsync(ParseStatement(ofxText), accountId);

    /// <summary>Commits a previously planned import.</summary>
    public Task<ImportSummary> CommitAsync(
        int accountId,
        ImportPreview preview,
        IReadOnlyList<ImportRowDecision>? decisions = null,
        bool reverseSigns = false,
        string? fileName = null) =>
        Import.CommitAsync(preview, new ImportRequest
        {
            AccountId = accountId,
            SourceFileName = fileName,
            ReverseSigns = reverseSigns,
            Rows = decisions ?? [],
        });

    /// <summary>Plans and commits in one step, accepting every default.</summary>
    public async Task<ImportSummary> ImportAsync(
        int accountId,
        string ofxText,
        string? fileName = null)
    {
        ImportPreview preview = await PrepareAsync(accountId, ofxText);
        return await CommitAsync(accountId, preview, fileName: fileName);
    }

    /// <summary>Reads a transaction straight from the database, bypassing the services.</summary>
    public async Task<Transaction?> ReadTransactionAsync(int id)
    {
        await using MyFinanceDbContext db = CreateContext();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.Transactions, t => t.Id == id);
    }

    public void Dispose()
    {
        _book.Dispose();
        _temp.Dispose();
    }
}
