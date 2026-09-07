using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Scheduling;
using MyFinance.Core.Enums;
using MyFinance.Core.Payees;
using MyFinance.Core.Primitives;
using MyFinance.Core.Progress;
using MyFinance.Import.Model;
using MyFinance.Import.Mny;

namespace MyFinance.Data.Services;

/// <summary>
/// Brings a Microsoft Money book across into this application.
/// </summary>
/// <remarks>
/// <para>
/// The reading and the writing are deliberately separate. <see cref="MoneyReader" /> produces
/// a faithful copy in Money's own terms; this turns that into accounts, categories, payees
/// and transactions here. Keeping them apart means the numbers can be checked against
/// Money's own screens before a single row is written.
/// </para>
/// <para>
/// It writes into an empty book only. Merging twenty thousand transactions into a book that
/// already has some would produce duplicates nobody could untangle afterwards, and there is
/// no undo for that worth the name — start a new book instead.
/// </para>
/// </remarks>
public sealed class MigrationService
{
    public const string BookNotEmpty = "migration.book_not_empty";
    public const string NothingToMigrate = "migration.nothing_to_migrate";

    private readonly IBookContextFactory _factory;

    public MigrationService(IBookContextFactory factory) => _factory = factory;

    /// <summary>
    /// Whether the book has nothing in it that a migration would collide with.
    /// </summary>
    /// <remarks>
    /// The seeded chart of accounts does not count: it is a starter the migration is meant to
    /// replace. Budgets do, because they are filed against categories that are about to be
    /// swapped out from underneath them.
    /// </remarks>
    private static async Task<bool> IsEmptyAsync(MyFinanceDbContext db, CancellationToken cancellationToken) =>
        !await db.Accounts.AnyAsync(cancellationToken).ConfigureAwait(false)
        && !await db.Transactions.AnyAsync(cancellationToken).ConfigureAwait(false)
        && !await db.BudgetLines.AnyAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Reads a Money file and says what it holds, writing nothing.</summary>
    public async Task<MigrationPreview> PreviewAsync(
        string path,
        MigrationOptions? options = null,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        options ??= MigrationOptions.Default;

        // The file is read in one pass with no count to report until it is done, so this is
        // honestly indeterminate rather than a percentage nobody can compute.
        progress?.Report(WorkProgress.Starting($"Reading {Path.GetFileName(path)}"));

        MoneyBook book = await Task.Run(() => MoneyReader.Read(path), cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Looking at what is in it"));

        await using MyFinanceDbContext db = _factory.CreateContext();

        bool empty = await IsEmptyAsync(db, cancellationToken).ConfigureAwait(false);

        return new MigrationPreview
        {
            FileName = Path.GetFileName(path),
            Book = book,
            Accounts = Describe(book, options),
            Diagnostics = book.Diagnostics,
            TargetBookIsEmpty = empty,
        };
    }

    private static IReadOnlyList<MigrationAccountPreview> Describe(MoneyBook book, MigrationOptions options)
    {
        var rows = new List<MigrationAccountPreview>();

        foreach (MoneyAccount account in Selected(book, options))
        {
            List<MoneyTransaction> kept = [.. Kept(book, account.Id, options)];

            rows.Add(new MigrationAccountPreview(
                account.Id,
                account.Name,
                account.Type,
                account.IsClosed,
                kept.Count,
                account.OpeningBalance + Money.Sum(kept.Select(t => t.Amount))));
        }

        return rows;
    }

    private static IEnumerable<MoneyAccount> Selected(MoneyBook book, MigrationOptions options) =>
        book.Accounts.Where(a => options.IncludeClosedAccounts || !a.IsClosed);

    /// <summary>The transactions of one account that the options say to carry over.</summary>
    private static IEnumerable<MoneyTransaction> Kept(
        MoneyBook book,
        int accountId,
        MigrationOptions options) =>
        book.TopLevelTransactions.Where(t =>
            t.AccountId == accountId
            && (options.IncludeScheduledInstances || !t.IsScheduledInstance));

    /// <summary>Writes the whole book, or nothing at all.</summary>
    public async Task<MigrationSummary> MigrateAsync(
        MoneyBook book,
        MigrationOptions? options = null,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        options ??= MigrationOptions.Default;

        await using MyFinanceDbContext db = _factory.CreateContext();

        if (!await IsEmptyAsync(db, cancellationToken).ConfigureAwait(false))
        {
            throw new BookValidationException(
                BookNotEmpty,
                "This book already has accounts, transactions or budgets in it. A Money file can only be brought into an empty book — create a new one and migrate into that.");
        }

        List<MoneyAccount> accounts = [.. Selected(book, options)];

        if (accounts.Count == 0)
        {
            throw new BookValidationException(
                NothingToMigrate,
                "There are no accounts to bring across from that file.");
        }

        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Forty thousand rows go in here. Change tracking on every one of them turns a
        // migration that takes seconds into one that takes minutes.
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        var diagnostics = new List<ImportDiagnostic>(book.Diagnostics);

        progress?.Report(WorkProgress.Starting("Replacing the starter categories"));
        await ClearStarterCategoriesAsync(db, diagnostics, cancellationToken).ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Writing categories"));
        Dictionary<int, int> categoryMap = await WriteCategoriesAsync(db, book, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Writing payees"));
        Dictionary<int, int> payeeMap = await WritePayeesAsync(db, book, categoryMap, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Writing accounts"));
        Dictionary<int, int> accountMap = await WriteAccountsAsync(db, accounts, cancellationToken)
            .ConfigureAwait(false);

        await WriteMerchantCodesAsync(db, book, categoryMap, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        (int written, int splits, int transfers) = await WriteTransactionsAsync(
            db, book, options, accountMap, categoryMap, payeeMap, diagnostics, progress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Writing holdings"));
        int holdings = await WriteHoldingsAsync(db, book, accountMap, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Writing bills"));
        (int bills, IReadOnlyList<UnconvertedBill> unconverted) = await WriteScheduledAsync(
            db, book, accountMap, categoryMap, payeeMap, cancellationToken).ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Saving"));
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        db.ChangeTracker.AutoDetectChangesEnabled = true;

        return new MigrationSummary
        {
            AccountsCreated = accountMap.Count,
            CategoriesCreated = categoryMap.Count,
            PayeesCreated = payeeMap.Values.Distinct().Count(),
            TransactionsCreated = written,
            SplitsCreated = splits,
            TransfersLinked = transfers,
            BillsCreated = bills,
            HoldingsCreated = holdings,
            UnconvertedBills = unconverted,
            Diagnostics = diagnostics,
            Accounts = Describe(book, options),
        };
    }

    /// <summary>
    /// Brings securities, positions and recorded prices across.
    /// </summary>
    /// <remarks>
    /// <b>Quantity and prices only.</b> Money records no cost basis this reader can recover —
    /// its transaction table carries no quantity, price or cost column — so a migrated holding
    /// says what is owned and nothing about what it cost. The cost is left at zero and the
    /// summary says so, rather than a zero being left to read as though the shares were free.
    /// </remarks>
    private static async Task<int> WriteHoldingsAsync(
        MyFinanceDbContext db,
        MoneyBook book,
        Dictionary<int, int> accountMap,
        List<ImportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (book.Holdings.Count == 0 && book.Securities.Count == 0)
        {
            return 0;
        }

        var securityMap = new Dictionary<int, int>();

        foreach (MoneySecurity source in book.Securities)
        {
            var security = new Core.Entities.Security { Name = source.Name, Symbol = source.Symbol };
            db.Securities.Add(security);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            securityMap[source.Id] = security.Id;
        }

        foreach (MoneySecurityPrice price in book.SecurityPrices)
        {
            if (securityMap.TryGetValue(price.SecurityId, out int securityId))
            {
                db.SecurityPrices.Add(new SecurityPrice
                {
                    SecurityId = securityId,
                    AsOf = price.AsOf,
                    Price = Money.FromDecimal(price.Price),
                });
            }
        }

        int created = 0;

        foreach (MoneyHolding holding in book.Holdings)
        {
            if (!accountMap.TryGetValue(holding.AccountId, out int accountId)
                || !securityMap.TryGetValue(holding.SecurityId, out int securityId))
            {
                continue;
            }

            db.Holdings.Add(new Holding
            {
                AccountId = accountId,
                SecurityId = securityId,
                Quantity = Quantity.FromDecimal(holding.Quantity),
                CostBasis = Money.Zero,
            });

            created++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (created > 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                "mny.holding.no_cost",
                $"{created} holdings came across with their quantity, but Money does not record "
                + "what they cost in a form this can read. Enter the cost of each by hand, or "
                + "they will show as having been free."));
        }

        return created;
    }

    /// <summary>
    /// Converts Money's recurring bills into scheduled transactions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Converts only what is certain. A series whose repeat pattern this version has not
    /// verified against a real book is <b>not</b> converted to something plausible — it is
    /// listed for the user to set up by hand. A wrong due date is worse than no due date,
    /// which is the judgement <c>009-money-migration</c> made and this does not overturn.
    /// </para>
    /// <para>
    /// Occurrences already in the register are marked <b>entered</b> as the series is written.
    /// Money projects bills forward into its own register and those rows come across as
    /// transactions, so without this the first auto-entry after a migration would pay every
    /// backdated bill a second time.
    /// </para>
    /// </remarks>
    private static async Task<(int Created, IReadOnlyList<UnconvertedBill>)> WriteScheduledAsync(
        MyFinanceDbContext db,
        MoneyBook book,
        Dictionary<int, int> accountMap,
        Dictionary<int, int> categoryMap,
        Dictionary<int, int> payeeMap,
        CancellationToken cancellationToken)
    {
        var unconverted = new List<UnconvertedBill>();
        var payeeNames = book.Payees.ToDictionary(p => p.Id, p => p.Name);
        int created = 0;

        // Which due dates each series has already produced, by Money's own series key. Money
        // stamps hbillHead on every instance a bill generated, so this is exact — matching on
        // a repeated amount instead would miss every bill whose amount varies, which is most
        // of the estimated ones.
        ILookup<int, DateOnly> alreadyEntered = book.Transactions
            .Where(t => t.ScheduleHeadId is not null && !t.IsSplitPart)
            .ToLookup(t => t.ScheduleHeadId!.Value, t => t.Date);

        // Where the migrated book stops. Every due date on or before this was Money's to deal
        // with, and whatever it did or did not do about them is now history.
        DateOnly cutoff = book.Transactions.Count > 0
            ? book.Transactions.Max(t => t.Date)
            : DateOnly.FromDateTime(DateTime.UtcNow);

        // Which account types cannot hold a schedule at all — a loan or brokerage arrives
        // balance-only, and ScheduleService refuses to write against one.
        HashSet<int> unsupported = await db.Accounts
            .Where(a => a.Type == AccountType.UnsupportedImported)
            .Select(a => a.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (MoneyScheduled series in book.Scheduled)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string payeeName = series.PayeeId is int pid
                ? payeeNames.GetValueOrDefault(pid, "(no payee)")
                : "(no payee)";

            if (!accountMap.TryGetValue(series.AccountId, out int accountId))
            {
                unconverted.Add(new UnconvertedBill(
                    payeeName, series.Amount, "its account was not brought across"));
                continue;
            }

            if (unsupported.Contains(accountId))
            {
                unconverted.Add(new UnconvertedBill(
                    payeeName, series.Amount, "its account is not one this version can schedule against"));
                continue;
            }

            if (series.Frequency is not RecurrenceFrequency frequency)
            {
                unconverted.Add(new UnconvertedBill(
                    payeeName, series.Amount, "its repeat pattern could not be recognised"));
                continue;
            }

            var scheduled = new ScheduledTransaction
            {
                AccountId = accountId,
                PayeeId = series.PayeeId is int p && payeeMap.TryGetValue(p, out int mapped) ? mapped : null,
                Memo = series.Memo,
                Amount = series.Amount,
                Frequency = frequency,
                Interval = 1,
                StartDate = series.NextDue,
                EndKind = series.EndDate is not null
                    ? RecurrenceEndKind.OnDate
                    : series.OccurrenceCount is not null
                        ? RecurrenceEndKind.AfterOccurrences
                        : RecurrenceEndKind.Never,
                EndDate = series.EndDate,
                OccurrenceCount = series.OccurrenceCount,
                AutoEnter = series.DaysAheadToEnter is not null,
                DaysAheadToEnter = series.DaysAheadToEnter ?? 5,
                IsActive = true,
            };

            if (series.CategoryId is int cat && categoryMap.TryGetValue(cat, out int categoryId))
            {
                scheduled.Splits.Add(new ScheduledTransactionSplit
                {
                    CategoryId = categoryId,
                    Amount = series.Amount,
                    SortOrder = 0,
                });
            }

            // Settle every due date the migrated book already covers.
            //
            // Two states, and the distinction is the honest part. Where Money actually
            // generated an instance, the transaction came across with it and the occurrence is
            // **entered**. Where it did not — a bill Money was showing as overdue, sometimes by
            // years — the occurrence is **skipped**: it was due, it was not paid, and this
            // migration is not going to invent a payment for it.
            //
            // Without both, the first auto-entry after a migration writes the entire backlog
            // into the register as transactions that never happened. Money itself does not do
            // that — it lists them as overdue and waits, which is what "n occurrences past
            // due" on its own Bills screen means.
            if (series.HeadId is int headId)
            {
                var entered = alreadyEntered[headId].ToHashSet();

                var rule = new RecurrenceRule
                {
                    Frequency = frequency,
                    Interval = 1,
                    StartDate = series.NextDue,
                    EndKind = RecurrenceEndKind.Never,
                };

                foreach (DateOnly due in RecurrenceCalculator.Occurrences(rule, null, cutoff))
                {
                    scheduled.Occurrences.Add(new ScheduleOccurrence
                    {
                        DueDate = due,
                        State = entered.Contains(due)
                            ? ScheduleOccurrenceState.Entered
                            : ScheduleOccurrenceState.Skipped,
                    });
                }
            }

            db.ScheduledTransactions.Add(scheduled);
            created++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (created, unconverted);
    }

    /// <summary>
    /// Removes the starter chart of accounts a new book is seeded with.
    /// </summary>
    /// <remarks>
    /// A migration brings the user's own categories, built up over decades and already
    /// meaning something to them. Keeping the seeded tree alongside would leave two
    /// half-charts — a "Groceries" they have used for twenty years next to an empty one this
    /// application invented — and every later report split between them. The book is known to
    /// be empty at this point, so nothing is filed under these and nothing is lost.
    /// </remarks>
    private static async Task ClearStarterCategoriesAsync(
        MyFinanceDbContext db,
        List<ImportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        List<Category> seeded = await db.Categories.ToListAsync(cancellationToken).ConfigureAwait(false);

        if (seeded.Count == 0)
        {
            return;
        }

        // Children first: a parent still referenced by one of its own children cannot go.
        db.Categories.RemoveRange(seeded.Where(c => c.ParentId is not null));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.Categories.RemoveRange(seeded.Where(c => c.ParentId is null));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        diagnostics.Add(new ImportDiagnostic(
            ImportSeverity.Info,
            "migration.starter_categories_replaced",
            $"The {seeded.Count} starter categories were replaced by the ones from your Money file."));
    }

    /// <summary>
    /// Writes the category tree, flattening Money's three levels into this application's two.
    /// </summary>
    /// <remarks>
    /// Money's top level is only ever the two roots, INCOME and EXPENSE, which this
    /// application records as a flag on the category rather than as rows of their own. The
    /// remaining two levels line up exactly, so nothing has to be invented or dropped.
    /// </remarks>
    private static async Task<Dictionary<int, int>> WriteCategoriesAsync(
        MyFinanceDbContext db,
        MoneyBook book,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<int, int>();

        // Headings first, so a subcategory always has a parent to point at.
        List<MoneyCategory> parents = [.. book.Categories.Where(c => c.Level == 1).OrderBy(c => c.Name)];
        List<MoneyCategory> children = [.. book.Categories.Where(c => c.Level >= 2).OrderBy(c => c.Name)];

        var written = new Dictionary<int, Category>();
        int order = 0;

        foreach (MoneyCategory source in parents)
        {
            var entity = new Category
            {
                Name = source.Name,
                Kind = source.Kind,
                SortOrder = order++,
            };

            db.Categories.Add(entity);
            written[source.Id] = entity;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach ((int sourceId, Category entity) in written)
        {
            map[sourceId] = entity.Id;
        }

        foreach (MoneyCategory source in children)
        {
            // A subcategory whose heading did not come across would be orphaned; give it the
            // heading's own place rather than dropping the category and its history with it.
            int? parentId = source.ParentId is int p && map.TryGetValue(p, out int mapped)
                ? mapped
                : null;

            var entity = new Category
            {
                Name = source.Name,
                Kind = source.Kind,
                ParentId = parentId,
                SortOrder = order++,
            };

            db.Categories.Add(entity);
            written[source.Id] = entity;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach ((int sourceId, Category entity) in written)
        {
            map[sourceId] = entity.Id;
        }

        return map;
    }

    /// <summary>
    /// Writes payees, folding together any that normalize to the same name.
    /// </summary>
    /// <remarks>
    /// Money lets two payees differ only by punctuation or case, and a book built up over
    /// twenty-five years accumulates plenty. Both source ids point at the surviving payee so
    /// no transaction loses its payee in the process.
    /// </remarks>
    private static async Task<Dictionary<int, int>> WritePayeesAsync(
        MyFinanceDbContext db,
        MoneyBook book,
        Dictionary<int, int> categoryMap,
        CancellationToken cancellationToken)
    {
        var byNormalized = new Dictionary<string, Payee>(StringComparer.Ordinal);
        var sourceToEntity = new Dictionary<int, Payee>();

        foreach (MoneyPayee source in book.Payees.OrderBy(p => p.Id))
        {
            string normalized = PayeeNormalizer.Normalize(source.Name);

            if (normalized.Length == 0)
            {
                continue;
            }

            if (!byNormalized.TryGetValue(normalized, out Payee? entity))
            {
                entity = new Payee
                {
                    Name = source.Name,
                    NormalizedName = normalized,
                    IsActive = !source.IsHidden,
                };

                byNormalized[normalized] = entity;
                db.Payees.Add(entity);
            }

            sourceToEntity[source.Id] = entity;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var map = new Dictionary<int, int>(sourceToEntity.Count);

        foreach ((int sourceId, Payee entity) in sourceToEntity)
        {
            map[sourceId] = entity.Id;
        }

        await WritePayeeMemoryAsync(db, book, categoryMap, map, cancellationToken).ConfigureAwait(false);
        return map;
    }

    /// <summary>
    /// Records what each payee was last filed under, so the next import categorizes itself.
    /// </summary>
    /// <remarks>
    /// This is the part that makes a migrated book immediately useful rather than merely
    /// complete: twenty-five years of decisions about which category a shop belongs in
    /// become the memory that fills in the next statement.
    /// </remarks>
    private static async Task WritePayeeMemoryAsync(
        MyFinanceDbContext db,
        MoneyBook book,
        Dictionary<int, int> categoryMap,
        Dictionary<int, int> payeeMap,
        CancellationToken cancellationToken)
    {
        var latest = new Dictionary<int, (DateOnly Date, int CategoryId, Money Amount)>();

        foreach (MoneyTransaction transaction in book.Transactions)
        {
            if (transaction.PayeeId is not int sourcePayee
                || transaction.CategoryId is not int sourceCategory
                || !payeeMap.TryGetValue(sourcePayee, out int payeeId)
                || !categoryMap.TryGetValue(sourceCategory, out int categoryId))
            {
                continue;
            }

            if (!latest.TryGetValue(payeeId, out (DateOnly Date, int CategoryId, Money Amount) held)
                || transaction.Date >= held.Date)
            {
                latest[payeeId] = (transaction.Date, categoryId, transaction.Amount);
            }
        }

        Dictionary<int, Payee> payees = await db.Payees
            .ToDictionaryAsync(p => p.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach ((int payeeId, (DateOnly _, int categoryId, Money amount)) in latest)
        {
            if (payees.TryGetValue(payeeId, out Payee? payee))
            {
                payee.LastCategoryId = categoryId;
                payee.LastAmount = amount;
            }
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<int, int>> WriteAccountsAsync(
        MyFinanceDbContext db,
        List<MoneyAccount> accounts,
        CancellationToken cancellationToken)
    {
        var written = new Dictionary<int, Account>();
        int order = 0;

        foreach (MoneyAccount source in accounts)
        {
            var entity = new Account
            {
                Name = source.Name,
                Type = source.Type,
                Institution = source.Institution,
                OpeningBalance = source.OpeningBalance,
                OpenedOn = source.OpenedOn,
                IsClosed = source.IsClosed,
                IsFavorite = source.IsFavorite,
                SortOrder = order++,
                Notes = "Brought across from Microsoft Money.",
            };

            db.Accounts.Add(entity);
            written[source.Id] = entity;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return written.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
    }

    /// <summary>
    /// Writes the register: transactions, their splits, and the links between transfer legs.
    /// </summary>
    /// <remarks>
    /// Transfers need two passes. A leg cannot point at its opposite until that opposite has
    /// an id, so the pairs are matched up and linked once everything is written.
    /// </remarks>
    private static async Task<(int Written, int Splits, int Transfers)> WriteTransactionsAsync(
        MyFinanceDbContext db,
        MoneyBook book,
        MigrationOptions options,
        Dictionary<int, int> accountMap,
        Dictionary<int, int> categoryMap,
        Dictionary<int, int> payeeMap,
        List<ImportDiagnostic> diagnostics,
        IProgress<WorkProgress>? progress,
        CancellationToken cancellationToken)
    {
        ILookup<int, MoneyTransaction> splitsByParent = book.SplitPartsByParent;
        var sequence = new Dictionary<(int Account, DateOnly Date), int>();
        var written = new Dictionary<int, Transaction>();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        int splitCount = 0;
        int skippedForAccount = 0;

        List<MoneyTransaction> ordered =
            [.. book.TopLevelTransactions.OrderBy(t => t.Date).ThenBy(t => t.Id)];

        // Every few hundred rather than every row: twenty thousand callbacks would flood the
        // dispatcher and slow down the very operation being measured.
        const int ReportEvery = 250;
        const string Stage = "Writing transactions";
        int seen = 0;

        progress?.Report(new WorkProgress(Stage, 0, ordered.Count));

        foreach (MoneyTransaction source in ordered)
        {
            // Checked here so a cancelled migration stops within a few rows rather than
            // running the whole book out. The surrounding transaction rolls back.
            if (++seen % ReportEvery == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new WorkProgress(Stage, seen, ordered.Count));
            }

            if (!accountMap.TryGetValue(source.AccountId, out int accountId))
            {
                skippedForAccount++;
                continue;
            }

            if (!options.IncludeScheduledInstances && source.IsScheduledInstance)
            {
                continue;
            }

            var key = (accountId, source.Date);
            sequence.TryGetValue(key, out int seq);
            sequence[key] = seq + 1;

            var entity = new Transaction
            {
                AccountId = accountId,
                Date = source.Date,
                Number = source.Number,
                PayeeId = source.PayeeId is int p && payeeMap.TryGetValue(p, out int payeeId)
                    ? payeeId
                    : null,
                Memo = source.Memo,
                Amount = source.Amount,
                ClearedStatus = source.Cleared,
                SequenceInDay = seq,
                CreatedUtc = now,
                ModifiedUtc = now,
            };

            List<MoneyTransaction> parts = [.. splitsByParent[source.Id].OrderBy(s => s.SplitIndex)];

            if (parts.Count > 0)
            {
                int order = 0;

                foreach (MoneyTransaction part in parts)
                {
                    entity.Splits.Add(new TransactionSplit
                    {
                        CategoryId = part.CategoryId is int c && categoryMap.TryGetValue(c, out int split)
                            ? split
                            : null,
                        Amount = part.Amount,
                        Memo = part.Memo,
                        SortOrder = order++,
                    });
                }

                splitCount += parts.Count;
            }
            else
            {
                // Every transaction carries at least one split, so the register has one shape
                // to read rather than two.
                entity.Splits.Add(new TransactionSplit
                {
                    CategoryId = source.CategoryId is int c && categoryMap.TryGetValue(c, out int mapped)
                        ? mapped
                        : null,
                    Amount = source.Amount,
                    SortOrder = 0,
                });
            }

            db.Transactions.Add(entity);
            written[source.Id] = entity;
        }

        progress?.Report(new WorkProgress(Stage, ordered.Count, ordered.Count));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Linking transfers"));
        int transfers = await LinkTransfersAsync(db, book, written, cancellationToken).ConfigureAwait(false);

        if (skippedForAccount > 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                "migration.orphan_transactions",
                $"{skippedForAccount} transactions belonged to an account that was not brought across and were left behind."));
        }

        return (written.Count, splitCount, transfers);
    }

    /// <summary>
    /// Brings Money's merchant-code table across.
    /// </summary>
    /// <remarks>
    /// A curated list somebody at Microsoft wrote once and every Money user inherited. It is
    /// worth keeping: it is the only source in the whole chain that can categorize a shop the
    /// user has genuinely never dealt with, and it costs a hundred and sixty rows.
    /// </remarks>
    private static async Task WriteMerchantCodesAsync(
        MyFinanceDbContext db,
        MoneyBook book,
        Dictionary<int, int> categoryMap,
        List<ImportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (book.MerchantCodes.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int written = 0;

        foreach (MoneyMerchantCode code in book.MerchantCodes)
        {
            // A code whose category did not come across would point at nothing.
            if (!categoryMap.TryGetValue(code.CategoryId, out int categoryId) || !seen.Add(code.Code))
            {
                continue;
            }

            db.MerchantCodeCategories.Add(new MerchantCodeCategory
            {
                Code = code.Code,
                CategoryId = categoryId,
            });

            written++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (written > 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Info,
                "migration.merchant_codes",
                $"{written} merchant industry codes came across, for categorizing shops you have not used before."));
        }
    }

    /// <summary>Pairs the two legs of each transfer and points them at one another.</summary>
    private static async Task<int> LinkTransfersAsync(
        MyFinanceDbContext db,
        MoneyBook book,
        Dictionary<int, Transaction> written,
        CancellationToken cancellationToken)
    {
        // Keyed on the day, the size of the transfer and the pair of accounts, which is what
        // Money itself records. Several identical transfers on one day pair off in turn.
        var waiting = new Dictionary<(DateOnly, long, int, int), Queue<MoneyTransaction>>();

        foreach (MoneyTransaction leg in book.TopLevelTransactions
            .Where(t => t.IsTransfer && !t.IsTransferSource && t.LinkedAccountId is not null))
        {
            var key = (leg.Date, leg.Amount.Abs().MinorUnits, leg.AccountId, leg.LinkedAccountId!.Value);

            if (!waiting.TryGetValue(key, out Queue<MoneyTransaction>? queue))
            {
                queue = new Queue<MoneyTransaction>();
                waiting[key] = queue;
            }

            queue.Enqueue(leg);
        }

        int linked = 0;

        foreach (MoneyTransaction source in book.TopLevelTransactions
            .Where(t => t.IsTransfer && t.IsTransferSource && t.LinkedAccountId is not null))
        {
            var key = (source.Date, source.Amount.Abs().MinorUnits, source.LinkedAccountId!.Value, source.AccountId);

            if (!waiting.TryGetValue(key, out Queue<MoneyTransaction>? queue) || queue.Count == 0)
            {
                continue;
            }

            MoneyTransaction destination = queue.Dequeue();

            if (!written.TryGetValue(source.Id, out Transaction? from)
                || !written.TryGetValue(destination.Id, out Transaction? to))
            {
                continue;
            }

            from.TransferPeerId = to.Id;
            to.TransferPeerId = from.Id;
            linked++;
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return linked;
    }
}
