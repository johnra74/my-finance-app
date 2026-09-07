using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Accounts;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Core.Validation;

namespace MyFinance.Data.Services;

/// <summary>Creates, edits, closes and deletes accounts, and computes the account list.</summary>
public sealed class AccountService
{
    public const string NameRequired = "account.name_required";
    public const string NameDuplicate = "account.name_duplicate";
    public const string NotFound = "account.not_found";
    public const string ReadOnly = "account.read_only";

    private readonly IBookContextFactory _factory;

    public AccountService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Accounts only, without balances — for pickers and the transfer dropdown.</summary>
    public async Task<IReadOnlyList<Account>> GetAllAsync(
        bool includeClosed = false,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.Accounts
            .AsNoTracking()
            .Where(a => includeClosed || !a.IsClosed)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Account?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The grouped, subtotalled account list.
    /// </summary>
    /// <remarks>
    /// Balances are aggregated in SQL rather than by loading every transaction. A book with
    /// years of history has tens of thousands of rows, and the Banking screen opens on every
    /// navigation — pulling them all back to sum them in memory would make the app feel slow
    /// for a number the database can produce directly.
    /// </remarks>
    public async Task<AccountListSummary> GetAccountListAsync(
        bool includeClosed = false,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<Account> accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => includeClosed || !a.IsClosed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (accounts.Count == 0)
        {
            return AccountListSummary.Empty;
        }

        IReadOnlyDictionary<int, AccountTotals> totals =
            await LoadTotalsAsync(db, cancellationToken).ConfigureAwait(false);

        // A transaction is uncategorized when no split on it carries a category.
        var uncategorized = await db.Transactions
            .AsNoTracking()
            .Where(t => !t.IsVoid && t.TransferPeerId == null && t.Splits.All(s => s.CategoryId == null))
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        var summaries = new List<AccountSummary>(accounts.Count);

        foreach (Account account in accounts)
        {
            totals.TryGetValue(account.Id, out AccountTotals total);
            uncategorized.TryGetValue(account.Id, out int needsCategory);

            summaries.Add(new AccountSummary
            {
                Account = account,
                CurrentBalance = account.OpeningBalance + Money.FromMinorUnits(total.Current),
                ClearedBalance = account.OpeningBalance + Money.FromMinorUnits(total.Cleared),
                TransactionCount = total.Count,
                UncategorizedCount = needsCategory,
            });
        }

        return AccountListBuilder.Build(summaries);
    }

    /// <summary>Per-account sums in minor units, as read straight from the database.</summary>
    private readonly record struct AccountTotals(long Current, long Cleared, int Count);

    /// <summary>
    /// Sums every account's register in one statement.
    /// </summary>
    /// <remarks>
    /// Hand-written SQL rather than LINQ because <c>Amount</c> reaches the database through a
    /// value converter: EF knows how to store and read a <see cref="Money"/> as an INTEGER,
    /// but it cannot translate <c>Sum</c> over one, and materializing years of transactions
    /// just to add them up would make the Banking screen slow to open.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<int, AccountTotals>> LoadTotalsAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<int, AccountTotals>();

        DbConnection connection = db.Database.GetDbConnection();
        bool openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT "AccountId",
                       SUM("Amount")                                                  AS "Current",
                       SUM(CASE WHEN "ClearedStatus" IN (1, 2) THEN "Amount" ELSE 0 END) AS "Cleared",
                       COUNT(*)                                                       AS "Rows"
                FROM "Transactions"
                WHERE "IsVoid" = 0
                GROUP BY "AccountId";
                """;

            await using DbDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results[reader.GetInt32(0)] = new AccountTotals(
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt32(3));
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }

        return results;
    }

    public async Task<int> CreateAsync(AccountDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using MyFinanceDbContext db = _factory.CreateContext();
        string name = (draft.Name ?? string.Empty).Trim();

        await GuardNameAsync(db, name, excludingId: null, cancellationToken).ConfigureAwait(false);

        // New accounts land at the bottom of their group unless the caller ordered them.
        int sortOrder = draft.SortOrder;
        if (sortOrder == 0)
        {
            int highest = await db.Accounts
                .Select(a => (int?)a.SortOrder)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0;
            sortOrder = highest + 1;
        }

        var account = new Account
        {
            Name = name,
            Type = draft.Type,
            Institution = Trim(draft.Institution),
            AccountNumberMasked = Trim(draft.AccountNumberMasked),
            OpeningBalance = draft.OpeningBalance,
            OpenedOn = draft.OpenedOn,
            CurrencyCode = string.IsNullOrWhiteSpace(draft.CurrencyCode) ? "USD" : draft.CurrencyCode.Trim(),
            IsClosed = draft.IsClosed,
            IsFavorite = draft.IsFavorite,
            SortOrder = sortOrder,
            Notes = Trim(draft.Notes),
            OfxAccountKey = Trim(draft.OfxAccountKey),
            OfxBankId = Trim(draft.OfxBankId),
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return account.Id;
    }

    public async Task UpdateAsync(AccountDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.Id is not int id)
        {
            throw new BookValidationException(NotFound, "This account has not been saved yet.");
        }

        await using MyFinanceDbContext db = _factory.CreateContext();
        Account account = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        string name = (draft.Name ?? string.Empty).Trim();
        await GuardNameAsync(db, name, excludingId: id, cancellationToken).ConfigureAwait(false);

        account.Name = name;
        account.Institution = Trim(draft.Institution);
        account.AccountNumberMasked = Trim(draft.AccountNumberMasked);
        account.OpeningBalance = draft.OpeningBalance;
        account.OpenedOn = draft.OpenedOn;
        account.IsFavorite = draft.IsFavorite;
        account.SortOrder = draft.SortOrder;
        account.Notes = Trim(draft.Notes);

        // Only ever set, never cleared. The account editor has no statement to derive these
        // from and leaves them null, and an ordinary edit must not throw away the link that
        // lets the next download recognise this account.
        if (Trim(draft.OfxAccountKey) is string key)
        {
            account.OfxAccountKey = key;
        }

        if (Trim(draft.OfxBankId) is string bankId)
        {
            account.OfxBankId = bankId;
        }

        // Type is not editable once history exists: changing a chequing account into a credit
        // card would silently flip the meaning of the sign on every transaction already
        // recorded against it.
        bool hasHistory = await db.Transactions
            .AnyAsync(t => t.AccountId == id, cancellationToken)
            .ConfigureAwait(false);

        if (!hasHistory)
        {
            account.Type = draft.Type;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes or reopens an account. Closed accounts keep their history and stay in reports;
    /// they simply drop out of pickers and out of the default account list.
    /// </summary>
    public async Task SetClosedAsync(int id, bool isClosed, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        Account account = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        account.IsClosed = isClosed;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        Account account = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        account.IsFavorite = isFavorite;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>How many transactions deleting this account would take with it.</summary>
    public async Task<int> CountTransactionsAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.Transactions
            .CountAsync(t => t.AccountId == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes an account and everything in its register.
    /// </summary>
    /// <remarks>
    /// The far leg of every transfer is deleted too. A transfer whose other half has gone is
    /// not a transfer any more — it is an unexplained amount sitting in another account's
    /// register — so leaving those behind would corrupt balances the user never touched.
    /// This is irreversible and the UI is expected to say how much history it will destroy.
    /// </remarks>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Account account = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        List<int> peerIds = await db.Transactions
            .Where(t => t.AccountId == id && t.TransferPeerId != null)
            .Select(t => t.TransferPeerId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (peerIds.Count > 0)
        {
            List<Transaction> peers = await db.Transactions
                .Where(t => peerIds.Contains(t.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Break the links first: the two rows reference each other, and the database
            // will not let either be removed while the other still points at it.
            foreach (Transaction peer in peers)
            {
                peer.TransferPeerId = null;
            }

            foreach (Transaction near in await db.Transactions
                .Where(t => t.AccountId == id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                near.TransferPeerId = null;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            db.Transactions.RemoveRange(peers);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-orders accounts to the given sequence of ids.</summary>
    public async Task ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);

        await using MyFinanceDbContext db = _factory.CreateContext();

        Dictionary<int, int> positions = orderedIds
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index + 1);

        List<Account> accounts = await db.Accounts
            .Where(a => positions.Keys.Contains(a.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Account account in accounts)
        {
            account.SortOrder = positions[account.Id];
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Account> RequireAsync(
        MyFinanceDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return account ?? throw new BookValidationException(NotFound, "That account no longer exists.");
    }

    private static async Task GuardNameAsync(
        MyFinanceDbContext db,
        string name,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new ValidationError(NameRequired, "An account needs a name."));
        }
        else
        {
            bool taken = await db.Accounts
                .AnyAsync(a => a.Name == name && (excludingId == null || a.Id != excludingId), cancellationToken)
                .ConfigureAwait(false);

            if (taken)
            {
                errors.Add(new ValidationError(
                    NameDuplicate,
                    $"An account called \"{name}\" already exists."));
            }
        }

        if (errors.Count > 0)
        {
            throw new BookValidationException(new ValidationResult(errors));
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
