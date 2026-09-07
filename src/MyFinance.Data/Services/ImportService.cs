using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Payees;
using MyFinance.Core.Primitives;
using MyFinance.Core.Progress;
using MyFinance.Core.Registers;
using MyFinance.Core.Validation;
using MyFinance.Import.Categorization;
using MyFinance.Import.Dedupe;
using MyFinance.Import.Model;
using MyFinance.Import.Ofx;
using MyFinance.Import.Payees;

namespace MyFinance.Data.Services;

/// <summary>
/// Loads a downloaded statement into an account, and takes it back out again.
/// </summary>
/// <remarks>
/// <para>
/// Split deliberately into a read-only planning pass and a single write. Everything the user
/// reviews is worked out before anything is written, so cancelling costs nothing, and the
/// commit is one database transaction — a partial import is far worse than no import,
/// because there is no way to tell by looking which half arrived.
/// </para>
/// <para>
/// Transactions are written directly rather than through <see cref="RegisterService"/>.
/// That path has no place for the bank's own reference or the batch link, and it issues a
/// query per row to find the intra-day sequence, which on a five-hundred-row statement is
/// five hundred round trips against an encrypted file. The same invariants are enforced
/// here, through the same <see cref="TransactionValidator"/>.
/// </para>
/// </remarks>
public sealed class ImportService
{
    public const string AccountNotFound = "import.account_not_found";
    public const string AccountReadOnly = "import.account_read_only";
    public const string BatchNotFound = "import.batch_not_found";
    public const string AlreadyReverted = "import.already_reverted";
    public const string BatchReconciled = "import.batch_reconciled";
    public const string NothingSelected = "import.nothing_selected";

    /// <summary>Matches the configured length of the memo column.</summary>
    private const int MemoMaxLength = 1024;

    private readonly IBookContextFactory _factory;
    private readonly ITextEmbedder _embedder;
    private readonly PayeeEmbeddingService _embeddings;
    private readonly SuggestionService _suggestions;

    /// <summary>
    /// Creates the service, optionally with an embedder for the similarity source.
    /// </summary>
    /// <remarks>
    /// The embedder defaults to one that does nothing, so every existing caller — and every
    /// machine where the model will not load — gets exactly the behaviour it had before.
    /// </remarks>
    public ImportService(IBookContextFactory factory, ITextEmbedder? embedder = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _embedder = embedder ?? NullTextEmbedder.Instance;
        _embeddings = new PayeeEmbeddingService(factory);
        _suggestions = new SuggestionService(factory, _embedder);
    }

    /// <summary>
    /// Works out what importing a statement into an account would do, writing nothing.
    /// </summary>
    public async Task<ImportPreview> PrepareAsync(
        ImportedStatement statement,
        int accountId,
        IReadOnlyList<ImportDiagnostic>? diagnostics = null,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statement);

        progress?.Report(WorkProgress.Starting("Reading the account"));

        await using MyFinanceDbContext db = _factory.CreateContext();

        Account account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(AccountNotFound, "That account no longer exists.");

        if (account.IsReadOnly)
        {
            throw new BookValidationException(
                AccountReadOnly,
                $"\"{account.Name}\" was brought across from Microsoft Money as a balance-only account, so nothing can be imported into it.");
        }

        // Everything the matching needs, in a fixed number of queries rather than one per
        // row. A statement of several hundred transactions is ordinary.
        List<Transaction> existingRows = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Payee)
            .Where(t => t.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(WorkProgress.Starting("Matching payees"));
        PayeeIndex payeeIndex = await LoadPayeeIndexAsync(db, cancellationToken).ConfigureAwait(false);

        List<Category> categories = await db.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, string> categoryNames = categories.ToDictionary(c => c.Id, c => c.FullName);

        // Matched on the rendered path, so a QIF's "Food:Coffee" finds this book's
        // "Food : Coffee" whatever punctuation the exporting program favoured.
        Dictionary<string, int> categoriesByPath = BuildPathIndex(categories);

        // The slow part on a large book: it reads every categorized transaction there is.
        // Shared with the transaction editor, so a statement imported shortly after somebody
        // filed a row by hand reuses the model rather than training a second copy of it.
        progress?.Report(WorkProgress.Starting("Learning from your history"));

        SuggestionContext suggestions =
            await _suggestions.GetContextAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RuleSpec> rules = suggestions.Rules;
        CategoryClassifier classifier = suggestions.Classifier;
        SimilarityIndex similar = suggestions.Similar;
        IReadOnlyDictionary<string, int> merchantCodes = suggestions.MerchantCodes;

        List<ExistingTransaction> existingKeys =
        [
            .. existingRows
                .Where(t => !t.IsVoid)
                .Select(t => new ExistingTransaction(t.Id, t.Date, t.Amount, t.FitId, t.Payee?.Name))
        ];

        progress?.Report(WorkProgress.Starting("Looking for duplicates"));
        IReadOnlyList<DuplicateVerdict> verdicts =
            DuplicateDetector.Detect(statement.Transactions, existingKeys);

        // One batch for the whole statement: the model is the slow part and calling it per
        // row would multiply the cost by the number of transactions.
        if (similar.IsUseful)
        {
            progress?.Report(WorkProgress.Starting("Recognising merchants"));
        }

        IReadOnlyList<float[]> vectors = similar.IsUseful
            ? await EmbedStatementAsync(statement, cancellationToken).ConfigureAwait(false)
            : [];

        var candidates = new List<ImportCandidate>(statement.Transactions.Count);

        const string SuggestStage = "Working out categories";
        progress?.Report(new WorkProgress(SuggestStage, 0, statement.Transactions.Count));

        for (int i = 0; i < statement.Transactions.Count; i++)
        {
            ImportedTransaction row = statement.Transactions[i];

            // The bank's NAME is the merchant; the MEMO is usually the location. Matching on
            // the name alone keeps the key stable when the branch text varies.
            CleanedDescriptor descriptor = DescriptorCleaner.Clean(row.Name ?? row.Memo);
            PayeeMatch payee = PayeeMatcher.Match(descriptor, payeeIndex);

            // A category the file named counts only when this book actually has it; naming
            // one it does not have is reported so the user can decide whether to create it.
            int? fileCategoryId = null;
            string? unmatchedPath = null;

            if (row.CategoryPath is string path)
            {
                if (categoriesByPath.TryGetValue(Normalize(path), out int matched))
                {
                    fileCategoryId = matched;
                }
                else
                {
                    unmatchedPath = path;
                }
            }

            List<ResolvedSplit> fileSplits =
            [
                .. row.Splits.Select(split => new ResolvedSplit(
                    split.CategoryPath is string p && categoriesByPath.TryGetValue(Normalize(p), out int id)
                        ? id
                        : null,
                    split.CategoryPath,
                    split.Memo,
                    split.Amount))
            ];

            CategorySuggestion suggestion = CategorySuggester.Suggest(
                new CategorySuggester.SuggestionInput(
                    accountId,
                    payee.Name,
                    descriptor.Raw,
                    row.Amount,
                    fileCategoryId,
                    payee.LastCategoryId,
                    i < vectors.Count ? vectors[i] : null,
                    row.MerchantCode),
                rules,
                classifier,
                similar: similar,
                merchantCodes: merchantCodes);

            if (i % 50 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new WorkProgress(SuggestStage, i, statement.Transactions.Count));
            }

            candidates.Add(new ImportCandidate
            {
                Index = i,
                Source = row,
                Descriptor = descriptor,
                Payee = payee,
                Duplicate = verdicts[i],
                Suggestion = suggestion,
                SuggestedCategoryName = suggestion.CategoryId is int id
                    ? categoryNames.GetValueOrDefault(id)
                    : null,
                UnmatchedCategoryPath = unmatchedPath,
                FileSplits = fileSplits,
            });
        }

        Money currentBalance = BalanceCalculator.CurrentBalance(account.OpeningBalance, existingRows);

        // Only the rows that would actually be written are evidence about the file's sign
        // convention; the duplicates are already accounted for in the current balance.
        List<ImportedTransaction> importable =
        [
            .. candidates.Where(c => c.IncludedByDefault).Select(c => c.Source)
        ];

        SignVerdict sign = SignConventionAnalyzer.Analyze(
            importable,
            statement.LedgerBalance?.Amount,
            currentBalance,
            statement.Account.Kind);

        return new ImportPreview
        {
            Account = account,
            Statement = statement,
            Candidates = candidates,
            Sign = sign,
            CurrentBalance = currentBalance,
            Diagnostics = diagnostics ?? [],
        };
    }

    /// <summary>
    /// Writes the reviewed rows into the register as one batch.
    /// </summary>
    public async Task<ImportSummary> CommitAsync(
        ImportPreview preview,
        ImportRequest request,
        IProgress<WorkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(request);

        progress?.Report(WorkProgress.Starting("Writing transactions"));

        Dictionary<int, ImportRowDecision> decisions = request.Rows
            .GroupBy(r => r.Index)
            .ToDictionary(g => g.Key, g => g.Last());

        List<ImportCandidate> chosen =
        [
            .. preview.Candidates.Where(c =>
                c.CanBeIncluded
                && (decisions.TryGetValue(c.Index, out ImportRowDecision? d) ? d.Include : c.IncludedByDefault))
        ];

        if (chosen.Count == 0)
        {
            throw new BookValidationException(
                NothingSelected,
                "No transactions were selected, so there is nothing to import.");
        }

        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Account account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(AccountNotFound, "That account no longer exists.");

        if (account.IsReadOnly)
        {
            throw new BookValidationException(
                AccountReadOnly,
                $"\"{account.Name}\" cannot be written to.");
        }

        // Re-read inside the transaction: another import may have run since the preview was
        // built, and a collision here would break the unique index and abort everything.
        HashSet<string> knownFitIds = await LoadFitIdsAsync(db, account.Id, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, int> createdCategories = request.CreateMissingCategories
            ? await CreateMissingCategoriesAsync(db, preview, cancellationToken).ConfigureAwait(false)
            : [];

        Dictionary<DateOnly, int> sequences = await LoadSequenceMaximaAsync(
            db, account.Id, cancellationToken).ConfigureAwait(false);

        var batch = new ImportBatch
        {
            SourceFileName = Truncate(request.SourceFileName, 512),
            Format = preview.Statement.Format,
            AccountId = account.Id,
            ImportedUtc = DateTimeOffset.UtcNow,
            PeriodStart = preview.Statement.FirstPosted,
            PeriodEnd = preview.Statement.LastPosted,
        };

        db.ImportBatches.Add(batch);

        var newPayees = new Dictionary<string, Payee>(StringComparer.Ordinal);
        var errors = new List<ValidationError>();
        var written = new List<Transaction>();

        // Which payee each row actually ended up on. The alias has to be recorded against
        // this rather than against the suggested name, or a correction the user made would
        // be remembered as pointing at the payee they rejected.
        var resolvedPayees = new Dictionary<int, Payee>();
        var firedRules = new List<int>();
        int skipped = 0;

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Oldest first, so a payee's remembered category ends up reflecting its most recent
        // transaction rather than whichever row happened to come last in the file.
        foreach (ImportCandidate candidate in chosen.OrderBy(c => c.Date).ThenBy(c => c.Index))
        {
            if (candidate.Source.ExternalId is string fitId && !knownFitIds.Add(fitId))
            {
                skipped++;
                continue;
            }

            decisions.TryGetValue(candidate.Index, out ImportRowDecision? decision);

            Money amount = request.ReverseSigns
                ? candidate.StatedAmount.Negated()
                : candidate.StatedAmount;

            string payeeName = decision?.PayeeName is string typed && typed.Trim().Length > 0
                ? typed.Trim()
                : candidate.Payee.Name;

            Payee? payee = await ResolvePayeeAsync(
                db, candidate, payeeName, newPayees, cancellationToken).ConfigureAwait(false);

            if (payee is not null)
            {
                resolvedPayees[candidate.Index] = payee;
            }

            int sequence = sequences.GetValueOrDefault(candidate.Date) + 1;
            sequences[candidate.Date] = sequence;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Date = candidate.Date,
                Number = Truncate(candidate.Source.CheckNumber, 32),
                Memo = Truncate(candidate.RawDescriptor, MemoMaxLength),
                Amount = amount,

                // QIF records a state per row and it must be honoured — a row the exporting
                // program had reconciled is not merely cleared. OFX has no such field, and
                // needs none: a downloaded item has by definition reached the bank, which is
                // exactly what Cleared means here and leaves the reconcile screen pre-ticked.
                ClearedStatus = candidate.Source.Cleared ?? ClearedStatus.Cleared,
                FitId = Truncate(candidate.Source.ExternalId, 128),
                SequenceInDay = sequence,
                CreatedUtc = now,
                ModifiedUtc = now,
                ImportBatch = batch,
            };

            if (payee is not null)
            {
                transaction.Payee = payee;
            }

            int? categoryId = decision is not null ? decision.CategoryId : candidate.SuggestedCategoryId;

            // A category the file named and the user asked us to create.
            if (categoryId is null
                && candidate.UnmatchedCategoryPath is string missing
                && createdCategories.TryGetValue(Normalize(missing), out int created))
            {
                categoryId = created;
            }

            if (candidate.Suggestion.Source == SuggestionSource.Rule
                && candidate.Suggestion.Rule is RuleSpec firedRule)
            {
                firedRules.Add(firedRule.Id);
            }

            AddSplits(transaction, candidate, categoryId, amount, request.ReverseSigns, createdCategories);

            ValidationResult validation = TransactionValidator.Validate(transaction);
            if (!validation.IsValid)
            {
                errors.AddRange(validation.Errors);
                continue;
            }

            db.Transactions.Add(transaction);
            written.Add(transaction);

            RememberPayee(payee, amount, categoryId);
        }

        // Reported together rather than one save at a time: a user with a bad file should see
        // every problem at once, and nothing at all should be written until they are fixed.
        if (errors.Count > 0)
        {
            throw new BookValidationException(new ValidationResult(errors));
        }

        int aliases = await RecordAliasesAsync(db, chosen, resolvedPayees, cancellationToken)
            .ConfigureAwait(false);

        CategorizationRuleService.RecordApplications(db, firedRules, now);

        batch.TransactionsAdded = written.Count;
        batch.DuplicatesSkipped = preview.TotalRows - written.Count;
        batch.PayeesCreated = newPayees.Count;
        batch.CategoriesCreated = createdCategories.Count;

        account.LastUpdatedOn = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        Money balanceAfter = preview.CurrentBalance
            + Money.Sum(written.Select(t => t.Amount));

        return new ImportSummary
        {
            BatchId = batch.Id,
            Added = written.Count,
            Skipped = preview.TotalRows - written.Count,
            PayeesCreated = newPayees.Count,
            AliasesRecorded = aliases,
            BalanceAfter = balanceAfter,
            LedgerBalance = preview.Statement.LedgerBalance?.Amount,
            CategoriesCreated = createdCategories.Count,
            RuleMatches = firedRules.Count,
        };
    }

    /// <summary>Past imports, most recent first.</summary>
    public async Task<IReadOnlyList<ImportHistoryEntry>> GetHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        // Ordered by key in SQL because SQLite cannot sort a DateTimeOffset column, then
        // settled in memory so the displayed order is by the timestamp the user sees.
        List<ImportBatch> batches = await db.ImportBatches
            .AsNoTracking()
            .Include(b => b.Account)
            .OrderByDescending(b => b.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        batches = [.. batches.OrderByDescending(b => b.ImportedUtc).ThenByDescending(b => b.Id)];

        Dictionary<int, int> remaining = await db.Transactions
            .AsNoTracking()
            .Where(t => t.ImportBatchId != null)
            .GroupBy(t => t.ImportBatchId!.Value)
            .Select(g => new { BatchId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BatchId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. batches.Select(b => new ImportHistoryEntry
            {
                Batch = b,
                AccountName = b.Account?.Name,
                RemainingTransactions = remaining.GetValueOrDefault(b.Id),
            })
        ];
    }

    /// <summary>
    /// Removes everything one import added.
    /// </summary>
    /// <remarks>
    /// The batch row itself is kept and marked reverted, so the history still records that
    /// the import happened and was undone. Payees and the descriptor mappings learned along
    /// the way are also kept: they may be referenced elsewhere by now, and they are the most
    /// valuable thing the import produced.
    /// </remarks>
    public async Task<int> RevertAsync(int batchId, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        ImportBatch batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(BatchNotFound, "That import is no longer recorded.");

        if (batch.IsReverted)
        {
            throw new BookValidationException(AlreadyReverted, "That import has already been undone.");
        }

        List<Transaction> transactions = await db.Transactions
            .Where(t => t.ImportBatchId == batchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int reconciled = transactions.Count(t => t.ClearedStatus == ClearedStatus.Reconciled);

        if (reconciled > 0)
        {
            // Reverting part of a batch would leave counters that no longer describe
            // anything, and removing reconciled rows would invalidate a balance the user has
            // already agreed with their bank.
            throw new BookValidationException(
                BatchReconciled,
                $"{reconciled} transaction{(reconciled == 1 ? " has" : "s have")} been reconciled since this import. Undoing it would invalidate that reconciliation — void or delete those individually instead.");
        }

        await RegisterService.RemoveWithPeersAsync(db, transactions, cancellationToken)
            .ConfigureAwait(false);

        batch.IsReverted = true;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return transactions.Count;
    }

    /// <summary>
    /// Finds the account a statement belongs to, or null when nothing matches confidently.
    /// </summary>
    /// <remarks>
    /// The keyed digest is exact and is what makes every import after the first one-click.
    /// The masked-number fallback exists for accounts that predate it, but cannot stand
    /// alone: several accounts at one bank routinely share their last four digits.
    /// </remarks>
    public async Task<int?> MatchAccountAsync(
        ImportedAccountInfo accountInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountInfo);

        await using MyFinanceDbContext db = _factory.CreateContext();

        byte[] secret = await SettingsService.GetOrCreateOfxSecretAsync(db, cancellationToken)
            .ConfigureAwait(false);

        string? key = OfxAccountKey.Compute(secret, accountInfo.BankId, accountInfo.AccountId);

        if (key is not null)
        {
            int? exact = await db.Accounts
                .AsNoTracking()
                .Where(a => a.OfxAccountKey == key)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (exact is not null)
            {
                return exact;
            }
        }

        string? tail = OfxAccountKey.Tail(accountInfo.AccountId);
        if (tail is null)
        {
            return null;
        }

        List<Account> candidates = await db.Accounts
            .AsNoTracking()
            .Where(a => !a.IsClosed && a.AccountNumberMasked != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Account> byTail =
        [
            .. candidates.Where(a =>
                string.Equals(OfxAccountKey.Tail(a.AccountNumberMasked), tail, StringComparison.Ordinal))
        ];

        if (byTail.Count == 1)
        {
            return byTail[0].Id;
        }

        // Several accounts share those digits. Narrowing by type is the only extra evidence
        // available, and if that is still ambiguous the user must choose.
        if (accountInfo.MappedType is AccountType type)
        {
            List<Account> byType = [.. byTail.Where(a => a.Type == type)];
            if (byType.Count == 1)
            {
                return byType[0].Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Records the digest that lets a future statement recognise this account by itself.
    /// </summary>
    public async Task LinkAccountAsync(
        int accountId,
        ImportedAccountInfo accountInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountInfo);

        await using MyFinanceDbContext db = _factory.CreateContext();

        Account account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(AccountNotFound, "That account no longer exists.");

        byte[] secret = await SettingsService.GetOrCreateOfxSecretAsync(db, cancellationToken)
            .ConfigureAwait(false);

        account.OfxAccountKey = OfxAccountKey.Compute(secret, accountInfo.BankId, accountInfo.AccountId);
        account.OfxBankId = Truncate(accountInfo.BankId, 64);

        account.AccountNumberMasked ??= Truncate(OfxAccountKey.Mask(accountInfo.AccountId), 64);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the transaction's category allocation.
    /// </summary>
    /// <remarks>
    /// A file that supplied several split lines has them kept, because carrying a ledger
    /// across from another program with its filing intact is the whole reason to read QIF at
    /// all — flattening a Costco receipt split between groceries and household back into one
    /// uncategorized row would throw away exactly what the user spent time recording.
    /// <para>
    /// The lines are only used when the user has not overridden the category themselves, and
    /// they are re-signed with the transaction so a reversed statement stays consistent.
    /// </para>
    /// </remarks>
    private static void AddSplits(
        Transaction transaction,
        ImportCandidate candidate,
        int? chosenCategoryId,
        Money amount,
        bool reverseSigns,
        Dictionary<string, int> createdCategories)
    {
        if (candidate.HasFileSplits && chosenCategoryId is null)
        {
            int order = 0;

            foreach (ResolvedSplit split in candidate.FileSplits)
            {
                int? splitCategory = split.CategoryId;

                if (splitCategory is null
                    && split.CategoryPath is string path
                    && createdCategories.TryGetValue(Normalize(path), out int created))
                {
                    splitCategory = created;
                }

                transaction.Splits.Add(new TransactionSplit
                {
                    CategoryId = splitCategory,
                    Amount = reverseSigns ? split.Amount.Negated() : split.Amount,
                    Memo = split.Memo,
                    SortOrder = order++,
                });
            }

            // The parser already refuses a split list that does not add up, so this holds —
            // but the invariant is load-bearing enough to be worth confirming rather than
            // assuming, and falling back costs nothing.
            if (Money.Sum(transaction.Splits.Select(s => s.Amount)) == amount)
            {
                return;
            }

            transaction.Splits.Clear();
        }

        transaction.Splits.Add(new TransactionSplit
        {
            CategoryId = chosenCategoryId,
            Amount = amount,
            SortOrder = 0,
        });
    }

    /// <summary>
    /// Creates the categories a file names that the book does not have.
    /// </summary>
    /// <remarks>
    /// Only ever called when the user ticked the box. It is how a ledger arrives from another
    /// program with its filing intact instead of every row landing uncategorized — but
    /// inventing dozens of categories unasked would be worse than leaving the rows blank, so
    /// it is never the default.
    /// </remarks>
    private static async Task<Dictionary<string, int>> CreateMissingCategoriesAsync(
        MyFinanceDbContext db,
        ImportPreview preview,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> missing = preview.MissingCategoryPaths;

        if (missing.Count == 0)
        {
            return [];
        }

        List<Category> existing = await db.Categories
            .Include(c => c.Parent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, int> byPath = BuildPathIndex(existing);
        var created = new Dictionary<string, int>(StringComparer.Ordinal);
        var staged = new Dictionary<string, Category>(StringComparer.Ordinal);

        foreach (string path in missing)
        {
            string[] parts = [.. path.Split(':', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim())];

            if (parts.Length == 0)
            {
                continue;
            }

            // Two levels only, as everywhere else. A deeper path from another program is
            // flattened onto its first two rather than refused.
            string parentName = parts[0];
            string? childName = parts.Length > 1 ? parts[1] : null;

            Category parent = await ResolveCategoryAsync(
                db, existing, staged, parentName, null, cancellationToken).ConfigureAwait(false);

            if (childName is null)
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                created[Normalize(parentName)] = parent.Id;
                continue;
            }

            Category child = await ResolveCategoryAsync(
                db, existing, staged, childName, parent, cancellationToken).ConfigureAwait(false);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            created[Normalize($"{parentName}:{childName}")] = child.Id;
        }

        // Anything that turned out to exist after all is not a creation.
        foreach (string path in created.Keys.ToList())
        {
            if (byPath.ContainsKey(path))
            {
                created.Remove(path);
            }
        }

        return created;
    }

    private static async Task<Category> ResolveCategoryAsync(
        MyFinanceDbContext db,
        List<Category> existing,
        Dictionary<string, Category> staged,
        string name,
        Category? parent,
        CancellationToken cancellationToken)
    {
        string key = $"{parent?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "root"}|{PayeeNormalizer.Normalize(name)}";

        if (staged.TryGetValue(key, out Category? already))
        {
            return already;
        }

        Category? found = existing.FirstOrDefault(c =>
            c.ParentId == parent?.Id
            && string.Equals(
                PayeeNormalizer.Normalize(c.Name),
                PayeeNormalizer.Normalize(name),
                StringComparison.Ordinal));

        if (found is not null)
        {
            staged[key] = found;
            return found;
        }

        var category = new Category
        {
            Name = name,
            Parent = parent,

            // A child takes its heading's side of the books, matching how categories behave
            // everywhere else in the application.
            Kind = parent?.Kind ?? CategoryKind.Expense,
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        existing.Add(category);
        staged[key] = category;
        return category;
    }

    /// <summary>
    /// Indexes the book's categories by their display path, punctuation and case ignored.
    /// </summary>
    /// <remarks>
    /// Exporting programs write the separator differently — Quicken uses "Food:Coffee" where
    /// this application renders "Food : Coffee" — so the comparison is made on the words
    /// alone. Without that, a ledger arriving from another program would match nothing and
    /// land entirely uncategorized.
    /// </remarks>
    private static Dictionary<string, int> BuildPathIndex(IEnumerable<Category> categories)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Category category in categories)
        {
            index.TryAdd(Normalize(category.FullName), category.Id);
        }

        return index;
    }

    /// <summary>Reduces a category path to its words, for comparison across programs.</summary>
    private static string Normalize(string path) =>
        string.Join(
            ':',
            path.Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => PayeeNormalizer.Normalize(part)));

    /// <summary>Embeds every row of a statement, in the order they appear.</summary>
    private async Task<IReadOnlyList<float[]>> EmbedStatementAsync(
        ImportedStatement statement,
        CancellationToken cancellationToken)
    {
        string[] texts =
        [
            .. statement.Transactions.Select(t => string.Join(
                " ",
                new[] { t.Name, t.Memo }
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .Distinct(StringComparer.OrdinalIgnoreCase))),
        ];

        // Off the calling thread: this is a few hundred forward passes and the caller may
        // well be a UI waiting to draw a preview.
        return await Task
            .Run(() => _embedder.Embed(texts, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<PayeeIndex> LoadPayeeIndexAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<KnownPayee> payees = await db.Payees
            .AsNoTracking()
            .Select(p => new KnownPayee(p.Id, p.Name, p.NormalizedName, p.LastCategoryId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<KnownAlias> aliases = await db.PayeeAliases
            .AsNoTracking()
            .Select(a => new KnownAlias(a.PayeeId, a.NormalizedPattern))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PayeeIndex(payees, aliases);
    }

    private static async Task<HashSet<string>> LoadFitIdsAsync(
        MyFinanceDbContext db,
        int accountId,
        CancellationToken cancellationToken)
    {
        List<string> ids = await db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId && t.FitId != null)
            .Select(t => t.FitId!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the highest intra-day sequence per date in one query.
    /// </summary>
    /// <remarks>
    /// The register asks the database for this one transaction at a time, which is right for
    /// a hand-typed entry and ruinous for a statement — one round trip per row against an
    /// encrypted file. Loaded once here and advanced in memory.
    /// </remarks>
    private static async Task<Dictionary<DateOnly, int>> LoadSequenceMaximaAsync(
        MyFinanceDbContext db,
        int accountId,
        CancellationToken cancellationToken) =>
        await db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .GroupBy(t => t.Date)
            .Select(g => new { Date = g.Key, Max = g.Max(t => t.SequenceInDay) })
            .ToDictionaryAsync(x => x.Date, x => x.Max, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Resolves the payee for a row, creating at most one per distinct name.
    /// </summary>
    /// <remarks>
    /// Rows matched to an existing payee use the id straight from the snapshot, so no query
    /// is issued at all. Genuinely new names go through the register's own resolver so that
    /// normalization behaves identically on both paths, and the result is cached so five
    /// hundred rows naming one merchant cost one lookup.
    /// </remarks>
    private static async Task<Payee?> ResolvePayeeAsync(
        MyFinanceDbContext db,
        ImportCandidate candidate,
        string payeeName,
        Dictionary<string, Payee> cache,
        CancellationToken cancellationToken)
    {
        string normalized = PayeeNormalizer.Normalize(payeeName);

        if (normalized.Length == 0)
        {
            return null;
        }

        if (cache.TryGetValue(normalized, out Payee? cached))
        {
            return cached;
        }

        // The user left the suggested name alone and it matched an existing payee, so the
        // snapshot already told us the id.
        if (candidate.Payee.PayeeId is int matchedId
            && string.Equals(payeeName, candidate.Payee.Name, StringComparison.Ordinal))
        {
            Payee? tracked = await db.Payees
                .FirstOrDefaultAsync(p => p.Id == matchedId, cancellationToken)
                .ConfigureAwait(false);

            if (tracked is not null)
            {
                cache[normalized] = tracked;
                return tracked;
            }
        }

        Payee? resolved = await PayeeService
            .ResolveAsync(db, payeeName, cancellationToken)
            .ConfigureAwait(false);

        if (resolved is not null)
        {
            cache[normalized] = resolved;
        }

        return resolved;
    }

    /// <summary>
    /// Remembers what a payee was last used for, mirroring what the register does.
    /// </summary>
    private static void RememberPayee(Payee? payee, Money amount, int? categoryId)
    {
        if (payee is null)
        {
            return;
        }

        payee.LastAmount = amount;

        if (categoryId is int id)
        {
            payee.LastCategoryId = id;
        }
    }

    /// <summary>
    /// Records the descriptor-to-payee mapping so the next download recognises it directly.
    /// </summary>
    /// <remarks>
    /// Keyed on the descriptor's stable form, which has the parts that vary between visits
    /// removed. Keyed on the raw text instead, every transaction would add an alias that
    /// could never match again.
    /// </remarks>
    private static async Task<int> RecordAliasesAsync(
        MyFinanceDbContext db,
        IReadOnlyList<ImportCandidate> candidates,
        Dictionary<int, Payee> resolvedPayees,
        CancellationToken cancellationToken)
    {
        List<string> keys =
        [
            .. candidates
                .Select(c => c.Descriptor.StableKey)
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.Ordinal)
        ];

        if (keys.Count == 0)
        {
            return 0;
        }

        HashSet<string> existing = await db.PayeeAliases
            .AsNoTracking()
            .Where(a => keys.Contains(a.NormalizedPattern))
            .Select(a => a.NormalizedPattern)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        int added = 0;

        foreach (ImportCandidate candidate in candidates)
        {
            string key = candidate.Descriptor.StableKey;

            if (key.Length == 0 || !existing.Add(key))
            {
                continue;
            }

            // Whichever payee this row actually landed on, correction included.
            if (!resolvedPayees.TryGetValue(candidate.Index, out Payee? payee))
            {
                continue;
            }

            db.PayeeAliases.Add(new PayeeAlias { Payee = payee, NormalizedPattern = key });
            added++;
        }

        return added;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
