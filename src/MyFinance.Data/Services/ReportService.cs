using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Investments;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;
using MyFinance.Core.Reporting;

namespace MyFinance.Data.Services;

/// <summary>Which report to run.</summary>
public enum ReportKind
{
    SpendingByCategory = 0,
    SpendingByPayee = 1,
    IncomeByCategory = 2,
    IncomeAndSpendingOverTime = 3,
    AccountBalances = 4,
    NetWorthOverTime = 5,
    TransactionDetail = 6,
    SpendingComparison = 7,
}

/// <summary>Everything one report run produced.</summary>
public sealed record ReportOutput
{
    public required ReportKind Kind { get; init; }

    public required string Title { get; init; }

    /// <summary>Set for the grouped reports.</summary>
    public GroupedReport? Grouped { get; init; }

    /// <summary>Set for the time-series reports.</summary>
    public TimeSeriesReport? Series { get; init; }

    /// <summary>Set for the account balances report.</summary>
    public IReadOnlyList<AccountBalanceRow>? Balances { get; init; }

    /// <summary>Set for net worth over time.</summary>
    public IReadOnlyList<NetWorthPoint>? NetWorth { get; init; }

    /// <summary>Set for the detail listing and for any drill-down.</summary>
    public IReadOnlyList<ReportEntry>? Detail { get; init; }

    /// <summary>Set for the comparison report.</summary>
    public ComparisonReport? Comparison { get; init; }

    /// <summary>Warning shown above the report when categories are missing.</summary>
    public string UncategorizedWarning => Grouped?.UncategorizedWarning ?? string.Empty;

    public bool HasUncategorized => Grouped?.HasUncategorized ?? false;
}

/// <summary>
/// Runs reports over the book.
/// </summary>
/// <remarks>
/// Loads the transactions once, flattens them to <see cref="ReportEntry"/>, and hands them
/// to the pure engine in Core. The database's only job here is to produce the rows; every
/// figure the user reads is computed by code that can be tested without one.
/// </remarks>
public sealed class ReportService
{
    private readonly IBookContextFactory _factory;

    public ReportService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Runs one report.</summary>
    public async Task<ReportOutput> RunAsync(
        ReportKind kind,
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using MyFinanceDbContext db = _factory.CreateContext();

        IReadOnlyList<ReportEntry> entries = await LoadEntriesAsync(db, cancellationToken)
            .ConfigureAwait(false);

        return kind switch
        {
            ReportKind.SpendingByCategory => Wrap(kind, ReportEngine.SpendingByCategory(entries, filter)),
            ReportKind.SpendingByPayee => Wrap(kind, ReportEngine.SpendingByPayee(entries, filter)),
            ReportKind.IncomeByCategory => Wrap(kind, ReportEngine.IncomeByCategory(entries, filter)),

            ReportKind.IncomeAndSpendingOverTime => Wrap(
                kind, ReportEngine.IncomeAndSpendingOverTime(entries, filter)),

            ReportKind.TransactionDetail => new ReportOutput
            {
                Kind = kind,
                Title = "Transactions",
                Detail = ReportEngine.Transactions(entries, filter),
            },

            ReportKind.AccountBalances =>
                await BalancesAsync(db, cancellationToken).ConfigureAwait(false),

            ReportKind.NetWorthOverTime =>
                await NetWorthAsync(db, entries, filter, cancellationToken).ConfigureAwait(false),

            ReportKind.SpendingComparison => Comparison(entries, filter),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown report."),
        };
    }

    /// <summary>
    /// The transactions behind one row of a report — the drill-down.
    /// </summary>
    /// <param name="filter">The report's own filter.</param>
    /// <param name="categoryId">Restricts to one category, or null for the uncategorized row.</param>
    /// <param name="payeeId">Restricts to one payee.</param>
    /// <param name="uncategorizedOnly">True when drilling into the uncategorized group.</param>
    public async Task<IReadOnlyList<ReportEntry>> DrillDownAsync(
        ReportFilter filter,
        int? categoryId = null,
        int? payeeId = null,
        bool uncategorizedOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using MyFinanceDbContext db = _factory.CreateContext();

        IReadOnlyList<ReportEntry> entries = await LoadEntriesAsync(db, cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<ReportEntry> matched = entries.Where(filter.Matches);

        if (uncategorizedOnly)
        {
            matched = matched.Where(e => e.IsUncategorized);
        }
        else if (categoryId is int category)
        {
            matched = matched.Where(e => e.CategoryId == category);
        }

        if (payeeId is int payee)
        {
            matched = matched.Where(e => e.PayeeId == payee);
        }

        return [.. matched.OrderBy(e => e.Date).ThenBy(e => e.TransactionId)];
    }

    /// <summary>Categories that have been used, for the report's category picker.</summary>
    public async Task<IReadOnlyList<CategoryListItem>> GetUsedCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<Category> categories = await db.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. categories
                .Select(c => new CategoryListItem { Category = c, FullName = c.FullName, UseCount = 0 })
                .OrderBy(c => c.FullName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>
    /// Flattens the book's transactions into the shape the report engine reads.
    /// </summary>
    /// <remarks>
    /// One row per split, because a transaction divided between two categories belongs to
    /// both. Every transaction carries at least one split, so nothing needs a fallback path.
    /// </remarks>
    internal static async Task<IReadOnlyList<ReportEntry>> LoadEntriesAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<Transaction> transactions = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Payee)
            .Include(t => t.Splits)
            .ThenInclude(s => s.Category)
            .ThenInclude(c => c!.Parent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The register rows that are the cash side of an investment activity. Identified from
        // the link the investment record already keeps, rather than guessed at from a payee
        // name or a missing category.
        HashSet<int> investmentCashLegs = [.. await db.InvestmentTransactions
            .AsNoTracking()
            .Where(t => t.CashTransactionId != null)
            .Select(t => t.CashTransactionId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)];

        var entries = new List<ReportEntry>(transactions.Count);

        foreach (Transaction transaction in transactions)
        {
            foreach (TransactionSplit split in transaction.Splits)
            {
                entries.Add(new ReportEntry
                {
                    TransactionId = transaction.Id,
                    AccountId = transaction.AccountId,
                    AccountName = transaction.Account?.Name ?? string.Empty,
                    AccountGroup = transaction.Account?.Group ?? AccountGroup.Other,
                    Date = transaction.Date,
                    PayeeId = transaction.PayeeId,
                    PayeeName = transaction.Payee?.Name,
                    CategoryId = split.CategoryId,
                    CategoryPath = split.Category?.FullName,
                    CategoryParent = split.Category?.Parent?.Name ?? split.Category?.Name,
                    CategoryKind = split.Category?.Kind,
                    Amount = split.Amount,
                    IsTransfer = transaction.TransferPeerId is not null,
                    IsInvestment = investmentCashLegs.Contains(transaction.Id),
                    IsVoid = transaction.IsVoid,
                    Memo = split.Memo ?? transaction.Memo,
                });
            }
        }

        return entries;
    }

    private static ReportOutput Wrap(ReportKind kind, GroupedReport report) =>
        new() { Kind = kind, Title = report.Title, Grouped = report };

    private static ReportOutput Wrap(ReportKind kind, TimeSeriesReport report) =>
        new() { Kind = kind, Title = report.Title, Series = report };

    private static ReportOutput Comparison(IReadOnlyList<ReportEntry> entries, ReportFilter filter)
    {
        // The window the user chose, against the one immediately before it of the same
        // length — which is the comparison anyone actually wants to make.
        DateOnly to = filter.To ?? DateOnly.FromDateTime(DateTime.Today);
        DateOnly from = filter.From ?? to.AddMonths(-1).AddDays(1);

        int days = to.DayNumber - from.DayNumber;
        DateOnly previousTo = from.AddDays(-1);
        DateOnly previousFrom = previousTo.AddDays(-days);

        ComparisonReport comparison = ReportEngine.CompareByCategory(
            entries,
            filter with { From = previousFrom, To = previousTo },
            filter,
            $"{previousFrom:d MMM yyyy} – {previousTo:d MMM yyyy}",
            $"{from:d MMM yyyy} – {to:d MMM yyyy}");

        return new ReportOutput
        {
            Kind = ReportKind.SpendingComparison,
            Title = comparison.Title,
            Comparison = comparison,
        };
    }

    private static async Task<ReportOutput> BalancesAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<Account> accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => !a.IsClosed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Transaction> transactions = await db.Transactions
            .AsNoTracking()
            .Where(t => !t.IsVoid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<AccountBalanceRow>(accounts.Count);

        foreach (Account account in accounts)
        {
            rows.Add(new AccountBalanceRow(
                account.Id,
                account.Name,
                account.Group,
                BalanceCalculator.CurrentBalance(
                    account.OpeningBalance,
                    transactions.Where(t => t.AccountId == account.Id))));
        }

        rows.Sort((left, right) =>
        {
            int byGroup = left.Group.CompareTo(right.Group);
            return byGroup != 0
                ? byGroup
                : string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        return new ReportOutput
        {
            Kind = ReportKind.AccountBalances,
            Title = "Account balances",
            Balances = rows,
        };
    }

    private static async Task<ReportOutput> NetWorthAsync(
        MyFinanceDbContext db,
        IReadOnlyList<ReportEntry> entries,
        ReportFilter filter,
        CancellationToken cancellationToken)
    {
        List<Account> accounts = await db.Accounts
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, (AccountGroup, Money)> openings = accounts.ToDictionary(
            a => a.Id,
            a => (a.Group, a.OpeningBalance));

        DateOnly to = filter.To ?? DateOnly.FromDateTime(DateTime.Today);
        DateOnly from = filter.From ?? to.AddYears(-1);

        // Holding activity and prices, so each point can be valued from what was held then.
        List<InvestmentMovement> investments = await db.InvestmentTransactions
            .AsNoTracking()
            .Where(t => t.Activity == InvestmentActivity.Buy
                     || t.Activity == InvestmentActivity.Sell
                     || t.Activity == InvestmentActivity.Reinvestment)
            .Select(t => new InvestmentMovement(
                t.AccountId,
                t.SecurityId,
                t.Date,
                t.Activity != InvestmentActivity.Sell,
                t.Quantity,
                t.Amount))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<SecurityPrice> prices = investments.Count == 0
            ? []
            : await db.SecurityPrices.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return new ReportOutput
        {
            Kind = ReportKind.NetWorthOverTime,
            Title = "Net worth over time",
            NetWorth = ReportEngine.NetWorthOverTime(
                openings, entries, from, to, filter.Period, investments, prices),
        };
    }
}
