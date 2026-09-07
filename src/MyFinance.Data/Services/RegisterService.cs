using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;
using MyFinance.Core.Validation;

namespace MyFinance.Data.Services;

/// <summary>Which transactions the register shows.</summary>
public sealed record RegisterFilter
{
    public static RegisterFilter All { get; } = new();

    /// <summary>Inclusive lower bound, or null for no lower bound.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Inclusive upper bound, or null for no upper bound.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Matched against payee name, memo and cheque number, case-insensitively.</summary>
    public string? Search { get; init; }

    /// <summary>Restricts to one cleared state, e.g. to find what has not cleared yet.</summary>
    public ClearedStatus? ClearedStatus { get; init; }

    /// <summary>Shows only transactions with no category on any split.</summary>
    public bool UncategorizedOnly { get; init; }

    public bool IsEmpty =>
        From is null && To is null && string.IsNullOrWhiteSpace(Search)
        && ClearedStatus is null && !UncategorizedOnly;
}

/// <summary>A loaded register: the visible rows plus the account's several balances.</summary>
public sealed record RegisterView
{
    public required Account Account { get; init; }

    /// <summary>Visible rows, in register order, each carrying the balance as at that row.</summary>
    public required IReadOnlyList<RegisterLine> Lines { get; init; }

    /// <summary>Every non-void transaction applied, whether or not the filter shows it.</summary>
    public required Money CurrentBalance { get; init; }

    /// <summary>Cleared and reconciled items only — the bank's view.</summary>
    public required Money ClearedBalance { get; init; }

    /// <summary>Locked in by the last completed reconciliation.</summary>
    public required Money ReconciledBalance { get; init; }

    /// <summary>Transactions in the account before the filter was applied.</summary>
    public required int TotalCount { get; init; }

    public int VisibleCount => Lines.Count;

    public bool IsFiltered => VisibleCount != TotalCount;
}

/// <summary>
/// Reads and writes register rows: the create, edit and delete path for transactions.
/// </summary>
/// <remarks>
/// Every write goes through here rather than through a context obtained elsewhere, because
/// the invariants that keep the books consistent — splits summing to the total, transfers
/// existing as a matched pair — cannot be enforced by the schema alone, and an invariant
/// enforced in only some of the write paths is not an invariant.
/// </remarks>
public sealed class RegisterService
{
    public const string NotFound = "transaction.not_found";
    public const string AccountNotFound = "transaction.account_not_found";
    public const string AccountReadOnly = "transaction.account_read_only";
    public const string TransferTargetSame = "transfer.same_account";
    public const string TransferTargetMissing = "transfer.target_missing";

    private readonly IBookContextFactory _factory;

    public RegisterService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// Loads one account's register.
    /// </summary>
    /// <remarks>
    /// The running balance is computed over the account's <b>whole</b> history and the filter
    /// applied afterwards. Recomputing it over only the visible rows would show a balance
    /// column that starts from zero whenever a date range or search is active, which reads as
    /// wrong even though every individual amount is right.
    /// </remarks>
    public async Task<RegisterView> GetRegisterAsync(
        int accountId,
        RegisterFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= RegisterFilter.All;

        await using MyFinanceDbContext db = _factory.CreateContext();

        Account account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(AccountNotFound, "That account no longer exists.");

        List<Transaction> transactions = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Payee)
            .Include(t => t.Splits)
            .ThenInclude(s => s.Category)
            .ThenInclude(c => c!.Parent)
            .Include(t => t.TransferPeer)
            .ThenInclude(p => p!.Account)
            .Where(t => t.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<RegisterLine> allLines =
            BalanceCalculator.BuildRegister(account.OpeningBalance, transactions);

        List<RegisterLine> visible = [.. allLines.Where(line => Matches(line.Transaction, filter))];

        return new RegisterView
        {
            Account = account,
            Lines = visible,
            CurrentBalance = BalanceCalculator.CurrentBalance(account.OpeningBalance, transactions),
            ClearedBalance = BalanceCalculator.ClearedBalance(account.OpeningBalance, transactions),
            ReconciledBalance = BalanceCalculator.ReconciledBalance(account.OpeningBalance, transactions),
            TotalCount = transactions.Count,
        };
    }

    /// <summary>Loads one transaction with everything the editor needs to show it.</summary>
    public async Task<Transaction?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.Transactions
            .AsNoTracking()
            .Include(t => t.Payee)
            .Include(t => t.Splits)
            .ThenInclude(s => s.Category)
            .ThenInclude(c => c!.Parent)
            .Include(t => t.TransferPeer)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates or updates a transaction, and the far leg when it is a transfer.
    /// </summary>
    /// <returns>The id of the saved transaction.</returns>
    public async Task<int> SaveAsync(TransactionDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await GuardAccountAsync(db, draft.AccountId, cancellationToken).ConfigureAwait(false);

        if (draft.TransferAccountId is int target)
        {
            if (target == draft.AccountId)
            {
                throw new BookValidationException(
                    TransferTargetSame,
                    "A transfer has to move money between two different accounts.");
            }

            await GuardAccountAsync(db, target, cancellationToken).ConfigureAwait(false);
        }

        Transaction entity;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (draft.Id is int id)
        {
            entity = await db.Transactions
                .Include(t => t.Splits)
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new BookValidationException(NotFound, "That transaction no longer exists.");
        }
        else
        {
            entity = new Transaction
            {
                AccountId = draft.AccountId,
                Date = draft.Date,
                CreatedUtc = now,
            };

            db.Transactions.Add(entity);
        }

        bool accountOrDateMoved = entity.AccountId != draft.AccountId || entity.Date != draft.Date;

        Payee? payee = await PayeeService
            .ResolveAsync(db, draft.PayeeName, cancellationToken)
            .ConfigureAwait(false);

        entity.AccountId = draft.AccountId;
        entity.Date = draft.Date;
        entity.Number = Trim(draft.Number);
        entity.Memo = Trim(draft.Memo);
        entity.Amount = draft.Amount;
        entity.ClearedStatus = draft.ClearedStatus;
        entity.IsVoid = draft.IsVoid;
        entity.ModifiedUtc = now;

        if (payee is null)
        {
            entity.PayeeId = null;
            entity.Payee = null;
        }
        else
        {
            entity.Payee = payee;
        }

        if (draft.Id is null || accountOrDateMoved)
        {
            entity.SequenceInDay = await NextSequenceAsync(
                db, draft.AccountId, draft.Date, cancellationToken).ConfigureAwait(false);
        }

        ApplySplits(db, entity, draft);

        ValidationResult validation = TransactionValidator.Validate(entity);
        if (!validation.IsValid)
        {
            throw new BookValidationException(validation);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SyncTransferPeerAsync(db, entity, draft, now, cancellationToken).ConfigureAwait(false);
        await RememberPayeeAsync(db, entity, draft, cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return entity.Id;
    }

    /// <summary>
    /// Deletes a transaction, taking the far leg of a transfer with it.
    /// </summary>
    /// <remarks>
    /// Deleting only the near leg would leave the other account holding an amount that no
    /// longer refers to anything, silently changing a balance the user was not looking at.
    /// </remarks>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Transaction entity = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That transaction no longer exists.");

        await RemoveWithPeersAsync(db, [entity], cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes transactions along with the far leg of any transfer among them.
    /// </summary>
    /// <remarks>
    /// Shared with the importer's undo, which has to do exactly this but inside its own
    /// transaction. Takes a context rather than opening one, because a caller undoing a
    /// three-hundred-row batch needs the whole thing to succeed or fail as a unit — looping
    /// over <see cref="DeleteAsync"/> would commit each row separately and could stop halfway.
    /// </remarks>
    internal static async Task RemoveWithPeersAsync(
        MyFinanceDbContext db,
        IReadOnlyList<Transaction> transactions,
        CancellationToken cancellationToken)
    {
        if (transactions.Count == 0)
        {
            return;
        }

        var doomed = transactions.ToDictionary(t => t.Id);

        List<int> peerIds =
        [
            .. transactions
                .Where(t => t.TransferPeerId is not null)
                .Select(t => t.TransferPeerId!.Value)
                .Distinct()
                .Where(id => !doomed.ContainsKey(id))
        ];

        List<Transaction> peers = peerIds.Count == 0
            ? []
            : await db.Transactions
                .Where(t => peerIds.Contains(t.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        // The two legs of a transfer reference each other, so the database refuses to remove
        // either while the other still points at it. Break every link and save first.
        foreach (Transaction transaction in transactions.Concat(peers))
        {
            transaction.TransferPeerId = null;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.Transactions.RemoveRange(peers);
        db.Transactions.RemoveRange(transactions);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes several transactions, resolving transfer peers for each.
    /// </summary>
    /// <remarks>
    /// Rows that have already gone are skipped rather than reported as an error: selecting
    /// both legs of a transfer and pressing delete is a perfectly reasonable thing to do, and
    /// removing the first leg takes the second with it.
    /// </remarks>
    /// <returns>How many transactions were actually removed.</returns>
    public async Task<int> DeleteManyAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        int removed = 0;

        foreach (int id in ids)
        {
            try
            {
                await DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                removed++;
            }
            catch (BookValidationException ex) when (ex.Errors.Any(e => e.Code == NotFound))
            {
            }
        }

        return removed;
    }

    /// <summary>
    /// Cycles or sets the cleared flag on a row — the single-click action in the register's
    /// "C" column.
    /// </summary>
    public async Task SetClearedStatusAsync(
        int id,
        ClearedStatus status,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        Transaction entity = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That transaction no longer exists.");

        entity.ClearedStatus = status;
        entity.ModifiedUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Voids or unvoids a row. A void transaction stays in the register for the audit trail
    /// but contributes nothing to any balance.
    /// </summary>
    public async Task SetVoidAsync(int id, bool isVoid, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Transaction entity = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That transaction no longer exists.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        entity.IsVoid = isVoid;
        entity.ModifiedUtc = now;

        // Both halves of a transfer void together, or the two accounts disagree about
        // whether the money moved.
        Transaction? peer = await FindPeerAsync(db, entity, cancellationToken).ConfigureAwait(false);
        if (peer is not null)
        {
            peer.IsVoid = isVoid;
            peer.ModifiedUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Assigns one category to a transaction, replacing whatever allocation it had. The
    /// one-click action from the uncategorized worklist.
    /// </summary>
    public async Task SetCategoryAsync(
        int id,
        int? categoryId,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        Transaction entity = await db.Transactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That transaction no longer exists.");

        db.TransactionSplits.RemoveRange(entity.Splits);
        entity.Splits.Clear();
        entity.Splits.Add(new TransactionSplit
        {
            CategoryId = categoryId,
            Amount = entity.Amount,
            SortOrder = 0,
        });

        entity.ModifiedUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Every transaction with no category, across all accounts — the backlog the register's
    /// triage view exists to clear.
    /// </summary>
    public async Task<IReadOnlyList<Transaction>> GetUncategorizedAsync(
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.Transactions
            .AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Payee)
            .Include(t => t.Splits)
            .Where(t => !t.IsVoid && t.TransferPeerId == null && t.Splits.All(s => s.CategoryId == null))
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool Matches(Transaction transaction, RegisterFilter filter)
    {
        if (filter.From is DateOnly from && transaction.Date < from)
        {
            return false;
        }

        if (filter.To is DateOnly to && transaction.Date > to)
        {
            return false;
        }

        if (filter.ClearedStatus is ClearedStatus status && transaction.ClearedStatus != status)
        {
            return false;
        }

        if (filter.UncategorizedOnly && !transaction.IsUncategorized)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string needle = filter.Search.Trim();

            bool hit =
                Contains(transaction.Payee?.Name, needle)
                || Contains(transaction.Memo, needle)
                || Contains(transaction.Number, needle)
                || Contains(transaction.Amount.ToString("N", CultureInfo.CurrentCulture), needle)
                || transaction.Splits.Any(s => Contains(s.Memo, needle) || Contains(s.Category?.FullName, needle));

            if (!hit)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Rewrites a transaction's category allocation from the draft.
    /// </summary>
    /// <remarks>
    /// Splits are replaced wholesale rather than diffed. There are rarely more than a handful
    /// per transaction, and reconciling an edited list against tracked entities is exactly
    /// the sort of code that quietly leaves an orphan behind.
    /// </remarks>
    private static void ApplySplits(MyFinanceDbContext db, Transaction entity, TransactionDraft draft)
    {
        db.TransactionSplits.RemoveRange(entity.Splits);
        entity.Splits.Clear();

        // A transfer is not spending, so it carries a single uncategorized split purely to
        // satisfy the invariant that every transaction has one.
        if (draft.IsTransfer || draft.Splits.Count == 0)
        {
            entity.Splits.Add(new TransactionSplit
            {
                CategoryId = null,
                Amount = draft.Amount,
                SortOrder = 0,
            });

            return;
        }

        int order = 0;
        foreach (SplitDraft split in draft.Splits)
        {
            entity.Splits.Add(new TransactionSplit
            {
                CategoryId = split.CategoryId,
                Amount = split.Amount,
                Memo = Trim(split.Memo),
                SortOrder = order++,
            });
        }
    }

    /// <summary>
    /// Creates, moves or removes the far leg so the pair always agrees.
    /// </summary>
    /// <remarks>
    /// Runs after the near leg has been saved because the two rows reference each other by
    /// id: neither can be written with its link set until the other exists.
    /// </remarks>
    private static async Task SyncTransferPeerAsync(
        MyFinanceDbContext db,
        Transaction entity,
        TransactionDraft draft,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Transaction? peer = await FindPeerAsync(db, entity, cancellationToken).ConfigureAwait(false);

        if (draft.TransferAccountId is not int targetAccountId)
        {
            if (peer is not null)
            {
                // It was a transfer and is not any more: unlink both, then drop the far leg.
                entity.TransferPeerId = null;
                peer.TransferPeerId = null;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                db.Transactions.Remove(peer);
            }

            return;
        }

        bool isNewPeer = peer is null;

        if (peer is null)
        {
            peer = new Transaction
            {
                AccountId = targetAccountId,
                Date = draft.Date,
                CreatedUtc = now,
            };

            db.Transactions.Add(peer);
        }
        else
        {
            await db.Entry(peer).Collection(p => p.Splits).LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        bool peerMoved = isNewPeer || peer.AccountId != targetAccountId || peer.Date != draft.Date;

        peer.AccountId = targetAccountId;
        peer.Date = draft.Date;
        peer.Amount = draft.Amount.Negated();
        peer.Memo = Trim(draft.Memo);
        peer.Number = Trim(draft.Number);
        peer.ClearedStatus = draft.ClearedStatus;
        peer.IsVoid = draft.IsVoid;
        peer.PayeeId = entity.PayeeId;
        peer.ModifiedUtc = now;

        if (peerMoved)
        {
            peer.SequenceInDay = await NextSequenceAsync(
                db, targetAccountId, draft.Date, cancellationToken).ConfigureAwait(false);
        }

        db.TransactionSplits.RemoveRange(peer.Splits);
        peer.Splits.Clear();
        peer.Splits.Add(new TransactionSplit
        {
            CategoryId = null,
            Amount = peer.Amount,
            SortOrder = 0,
        });

        ValidationResult pairing = TransactionValidator.ValidateTransferPair(entity, peer);
        if (!pairing.IsValid)
        {
            throw new BookValidationException(pairing);
        }

        // Save once with no links so the new row gets an id, then link both ways. Writing
        // them linked in one statement is impossible: each row's foreign key names the other.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        entity.TransferPeerId = peer.Id;
        peer.TransferPeerId = entity.Id;
    }

    private static async Task<Transaction?> FindPeerAsync(
        MyFinanceDbContext db,
        Transaction entity,
        CancellationToken cancellationToken)
    {
        if (entity.TransferPeerId is not int peerId)
        {
            return null;
        }

        return await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == peerId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records what this payee was last used for, so typing the name next time pre-fills the
    /// category and amount the way Microsoft Money does.
    /// </summary>
    private static async Task RememberPayeeAsync(
        MyFinanceDbContext db,
        Transaction entity,
        TransactionDraft draft,
        CancellationToken cancellationToken)
    {
        if (entity.PayeeId is not int payeeId || draft.IsTransfer)
        {
            return;
        }

        Payee? payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == payeeId, cancellationToken)
            .ConfigureAwait(false);

        if (payee is null)
        {
            return;
        }

        payee.LastAmount = draft.Amount;

        // Only a single-category transaction teaches a default; a split has no one category
        // to remember, and guessing one would pre-fill the wrong answer next time.
        if (draft.Splits.Count == 1 && draft.Splits[0].CategoryId is int categoryId)
        {
            payee.LastCategoryId = categoryId;
        }
    }

    private static async Task<int> NextSequenceAsync(
        MyFinanceDbContext db,
        int accountId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        int highest = await db.Transactions
            .Where(t => t.AccountId == accountId && t.Date == date)
            .Select(t => (int?)t.SequenceInDay)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

        return highest + 1;
    }

    private static async Task GuardAccountAsync(
        MyFinanceDbContext db,
        int accountId,
        CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            throw new BookValidationException(AccountNotFound, "That account no longer exists.");
        }

        if (account.IsReadOnly)
        {
            throw new BookValidationException(
                AccountReadOnly,
                $"\"{account.Name}\" was brought across from Microsoft Money as a balance-only account and cannot be edited.");
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
