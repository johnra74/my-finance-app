using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Export;
using MyFinance.Core.Progress;

namespace MyFinance.Data.Services;

/// <summary>
/// Writes the whole book out as one document that can be read without this application.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Money's file format is the reason this application exists, and until this
/// existed a MyFinance book was readable only from inside MyFinance — materially the same
/// trap. Reports already exported to CSV; that is a rendered report, not the book.
/// </para>
/// <para>
/// Nothing secret and nothing derived is written: there is no password or full account number
/// in the book to reintroduce, the OFX digest and its per-book secret are meaningless outside
/// the book they belong to, cached payee vectors are regenerable, and running balances are a
/// function of the rows that are already here.
/// </para>
/// </remarks>
public sealed class BookExportService
{
    private readonly IBookContextFactory _factory;

    public BookExportService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Builds the document for the whole book.</summary>
    public async Task<BookDocument> BuildAsync(
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        progress?.Report(WorkProgress.Starting("Reading accounts"));
        List<ExportAccount> accounts = await ReadAccountsAsync(db, cancellationToken).ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Reading categories"));
        List<ExportCategory> categories = await ReadCategoriesAsync(db, cancellationToken).ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Reading payees"));
        List<ExportPayee> payees = await ReadPayeesAsync(db, cancellationToken).ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Reading transactions"));
        List<ExportTransaction> transactions =
            await ReadTransactionsAsync(db, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Reading bills, budgets and rules"));
        List<ExportScheduled> scheduled = await ReadScheduledAsync(db, cancellationToken).ConfigureAwait(false);
        ExportBudget budget = await ReadBudgetAsync(db, cancellationToken).ConfigureAwait(false);
        List<ExportRule> rules = await ReadRulesAsync(db, cancellationToken).ConfigureAwait(false);
        List<ExportMerchantCode> merchantCodes = await ReadMerchantCodesAsync(db, cancellationToken).ConfigureAwait(false);
        List<ExportImportBatch> batches = await ReadImportBatchesAsync(db, cancellationToken).ConfigureAwait(false);

        return new BookDocument
        {
            ExportedUtc = Iso(DateTimeOffset.UtcNow),
            Application = new ExportApplication(
                "MyFinance",
                typeof(BookExportService).Assembly.GetName().Version?.ToString() ?? "unknown"),
            Accounts = accounts,
            Categories = categories,
            Payees = payees,
            Transactions = transactions,
            Scheduled = scheduled,
            Budgets = budget,
            Rules = rules,
            MerchantCodes = merchantCodes,
            ImportBatches = batches,
        };
    }

    /// <summary>
    /// Writes the document to a file.
    /// </summary>
    /// <remarks>
    /// Written to a <c>.partial</c> file and moved into place, so a failure part-way leaves no
    /// truncated file that looks like a complete export — the same discipline
    /// <see cref="Security.BackupService"/> applies, and for the same reason: a half-written
    /// artefact nobody notices is worse than none.
    /// </remarks>
    public async Task<string> ExportAsync(
        string path,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        BookDocument document = await BuildAsync(progress, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(WorkProgress.Starting("Writing the file"));

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string partial = path + ".partial";

        try
        {
            await File.WriteAllTextAsync(partial, document.ToJson(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    // -- Readers ------------------------------------------------------------------------

    private static async Task<List<ExportAccount>> ReadAccountsAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken) =>
        (await db.Accounts.AsNoTracking().OrderBy(a => a.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .Select(a => new ExportAccount
        {
            Id = a.Id,
            Name = a.Name,
            Type = a.Type.ToString(),
            Group = a.Group.ToString(),
            Institution = a.Institution,
            AccountNumberMasked = a.AccountNumberMasked,
            OpeningBalance = ExportMoney.Text(a.OpeningBalance),
            OpeningBalanceMinorUnits = a.OpeningBalance.MinorUnits,
            OpenedOn = Iso(a.OpenedOn),
            CurrencyCode = a.CurrencyCode,
            IsClosed = a.IsClosed,
            IsFavorite = a.IsFavorite,
            SortOrder = a.SortOrder,
            LastReconciledOn = Iso(a.LastReconciledOn),
            LastReconciledBalance = ExportMoney.Text(a.LastReconciledBalance),
            LastReconciledBalanceMinorUnits = a.LastReconciledBalance.MinorUnits,
            Notes = a.Notes,
        })
        .ToList();

    private static async Task<List<ExportCategory>> ReadCategoriesAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        // Loaded with parents so FullName renders without a second query per row.
        List<Category> rows = await db.Categories.AsNoTracking()
            .Include(c => c.Parent)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(c => new ExportCategory
        {
            Id = c.Id,
            Name = c.Name,
            FullName = c.FullName,
            ParentId = c.ParentId,
            Kind = c.Kind.ToString(),
            IsTaxRelated = c.IsTaxRelated,
            IsArchived = c.IsArchived,
            SortOrder = c.SortOrder,
        }).ToList();
    }

    private static async Task<List<ExportPayee>> ReadPayeesAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<Payee> rows = await db.Payees.AsNoTracking()
            .Include(p => p.Aliases)
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(p => new ExportPayee
        {
            Id = p.Id,
            Name = p.Name,
            NormalizedName = p.NormalizedName,
            LastCategoryId = p.LastCategoryId,
            LastAmount = ExportMoney.Text(p.LastAmount),
            LastAmountMinorUnits = ExportMoney.Minor(p.LastAmount),
            IsActive = p.IsActive,
            Notes = p.Notes,
            Aliases = p.Aliases.Select(a => a.NormalizedPattern).OrderBy(a => a, StringComparer.Ordinal).ToList(),
        }).ToList();
    }

    private static async Task<List<ExportTransaction>> ReadTransactionsAsync(
        MyFinanceDbContext db,
        IProgress<WorkProgress>? progress,
        CancellationToken cancellationToken)
    {
        int total = await db.Transactions.CountAsync(cancellationToken).ConfigureAwait(false);

        List<Transaction> rows = await db.Transactions.AsNoTracking()
            .Include(t => t.Splits)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var exported = new List<ExportTransaction>(rows.Count);
        int done = 0;

        foreach (Transaction t in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            exported.Add(new ExportTransaction
            {
                Id = t.Id,
                AccountId = t.AccountId,
                Date = Iso(t.Date)!,
                SequenceInDay = t.SequenceInDay,
                Amount = ExportMoney.Text(t.Amount),
                AmountMinorUnits = t.Amount.MinorUnits,
                PayeeId = t.PayeeId,
                Memo = t.Memo,
                Number = t.Number,
                ClearedStatus = t.ClearedStatus.ToString(),
                IsVoid = t.IsVoid,
                TransferPeerId = t.TransferPeerId,
                ImportBatchId = t.ImportBatchId,
                ScheduledTransactionId = t.ScheduledTransactionId,
                Splits = t.Splits.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).Select(ToSplit).ToList(),
            });

            if (++done % 500 == 0)
            {
                progress?.Report(new WorkProgress("Reading transactions", done, total));
            }
        }

        progress?.Report(new WorkProgress("Reading transactions", total, total));
        return exported;
    }

    private static ExportSplit ToSplit(TransactionSplit s) => new()
    {
        Id = s.Id,
        CategoryId = s.CategoryId,
        Amount = ExportMoney.Text(s.Amount),
        AmountMinorUnits = s.Amount.MinorUnits,
        Memo = s.Memo,
        SortOrder = s.SortOrder,
    };

    private static async Task<List<ExportScheduled>> ReadScheduledAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<ScheduledTransaction> rows = await db.ScheduledTransactions.AsNoTracking()
            .Include(s => s.Splits)
            .Include(s => s.Occurrences)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(s => new ExportScheduled
        {
            Id = s.Id,
            AccountId = s.AccountId,
            PayeeId = s.PayeeId,
            Memo = s.Memo,
            Amount = ExportMoney.Text(s.Amount),
            AmountMinorUnits = s.Amount.MinorUnits,
            IsEstimate = s.IsEstimate,
            PaymentMethod = s.PaymentMethod.ToString(),
            Frequency = s.Frequency.ToString(),
            Interval = s.Interval,
            StartDate = Iso(s.StartDate)!,
            EndKind = s.EndKind.ToString(),
            EndDate = Iso(s.EndDate),
            OccurrenceCount = s.OccurrenceCount,
            SecondDayOfMonth = s.SecondDayOfMonth,
            WeekendShift = s.WeekendShift.ToString(),
            AutoEnter = s.AutoEnter,
            DaysAheadToEnter = s.DaysAheadToEnter,
            IsActive = s.IsActive,
            Splits = s.Splits.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => new ExportSplit
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                Amount = ExportMoney.Text(p.Amount),
                AmountMinorUnits = p.Amount.MinorUnits,
                Memo = p.Memo,
                SortOrder = p.SortOrder,
            }).ToList(),
            Occurrences = s.Occurrences.OrderBy(o => o.DueDate).ThenBy(o => o.Id)
                .Select(o => new ExportScheduleOccurrence
                {
                    DueDate = Iso(o.DueDate)!,
                    State = o.State.ToString(),
                    TransactionId = o.TransactionId,
                    ActualAmount = ExportMoney.Text(o.ActualAmount),
                    ActualAmountMinorUnits = ExportMoney.Minor(o.ActualAmount),
                }).ToList(),
        }).ToList();
    }

    private static async Task<ExportBudget> ReadBudgetAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<BudgetLine> lines = await db.BudgetLines.AsNoTracking()
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<int> watched = await db.WatchedCategories.AsNoTracking()
            .OrderBy(w => w.SortOrder).ThenBy(w => w.Id)
            .Select(w => w.CategoryId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ExportBudget
        {
            Lines = lines.Select(b => new ExportBudgetLine
            {
                Id = b.Id,
                CategoryId = b.CategoryId,
                PeriodStart = Iso(b.PeriodStart)!,
                PeriodType = b.PeriodType.ToString(),
                Amount = ExportMoney.Text(b.Amount),
                AmountMinorUnits = b.Amount.MinorUnits,
                RollsOver = b.RollsOver,
                Notes = b.Notes,
            }).ToList(),
            WatchedCategoryIds = watched,
        };
    }

    /// <summary>
    /// Rules, <b>in priority order</b> — for a rule the order is part of the meaning, since
    /// the first match wins.
    /// </summary>
    private static async Task<List<ExportRule>> ReadRulesAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<CategorizationRule> rows = await db.CategorizationRules.AsNoTracking()
            .OrderBy(r => r.Priority).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => new ExportRule
        {
            Id = r.Id,
            Name = r.Name,
            Priority = r.Priority,
            MatchField = r.MatchField.ToString(),
            MatchKind = r.MatchKind.ToString(),
            Pattern = r.Pattern,
            IsCaseSensitive = r.IsCaseSensitive,
            AccountId = r.AccountId,
            TargetCategoryId = r.TargetCategoryId,
            TargetPayeeId = r.TargetPayeeId,
            IsEnabled = r.IsEnabled,
        }).ToList();
    }

    private static async Task<List<ExportMerchantCode>> ReadMerchantCodesAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken) =>
        (await db.MerchantCodeCategories.AsNoTracking()
            .OrderBy(m => m.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .Select(m => new ExportMerchantCode { Code = m.Code, CategoryId = m.CategoryId })
        .ToList();

    /// <summary>
    /// Import batches, present because transactions reference them.
    /// </summary>
    /// <remarks>
    /// Nearly omitted as internal undo machinery, which would have left every imported
    /// transaction carrying an id that resolves to nothing. Either the entity is exported or
    /// the id is not; of the two, keeping the provenance is the more useful answer — it says
    /// where a row came from, which nothing else in the document records.
    /// </remarks>
    private static async Task<List<ExportImportBatch>> ReadImportBatchesAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken) =>
        (await db.ImportBatches.AsNoTracking()
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .Select(b => new ExportImportBatch
        {
            Id = b.Id,
            SourceFileName = b.SourceFileName,
            Format = b.Format.ToString(),
            AccountId = b.AccountId,
            ImportedUtc = Iso(b.ImportedUtc),
            TransactionsAdded = b.TransactionsAdded,
            IsReverted = b.IsReverted,
        })
        .ToList();

    // -- Formatting ---------------------------------------------------------------------

    private static string Iso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static string? Iso(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Iso(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
