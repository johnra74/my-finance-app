using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Model;
using MyFinance.Import.Mny.Jet;

namespace MyFinance.Import.Mny;

/// <summary>
/// Reads a Microsoft Money file into <see cref="MoneyBook" />.
/// </summary>
/// <remarks>
/// <para>
/// This step is a faithful copy, not a translation: every value keeps Money's own meaning and
/// Money's own identifiers. Turning it into this application's shape happens afterwards, so
/// the reading can be checked against Money's own screens first.
/// </para>
/// <para>
/// Nothing here throws on a bad row. A twenty-five year old book will contain rows no longer
/// makes sense, and losing the other nineteen thousand over one of them would be absurd —
/// they are counted and reported instead.
/// </para>
/// </remarks>
public sealed class MoneyReader
{
    /// <summary>Money's account-type codes, as far as they can be relied on.</summary>
    private const int MoneyAccountTypeBank = 0;
    private const int MoneyAccountTypeCreditCard = 1;
    private const int MoneyAccountTypeCash = 2;
    private const int MoneyAccountTypeInvestment = 5;

    /// <summary>Marks a row as one leg of a transfer.</summary>
    private const int TransferFlag = 0x02;

    /// <summary>Marks the leg the money left.</summary>
    private const int TransferSourceFlag = 0x04;

    /// <summary>Marks a row generated from a scheduled series.</summary>
    private const int ScheduledFlag = 0x01;

    /// <summary>A row that is a real entry rather than the definition of a recurring one.</summary>
    private const int NotRecurring = -1;

    private readonly List<ImportDiagnostic> _diagnostics = [];

    public static MoneyBook Read(string path) => new MoneyReader().ReadFile(path);

    public static MoneyBook Read(JetDatabase database) => new MoneyReader().ReadDatabase(database);

    private MoneyBook ReadFile(string path) => ReadDatabase(JetDatabase.Open(path));

    private MoneyBook ReadDatabase(JetDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        JetTable accounts = MoneyTables.Require(database, "account", MoneyTables.AccountSignature);
        JetTable transactions = MoneyTables.Require(database, "transaction", MoneyTables.TransactionSignature);
        JetTable categories = MoneyTables.Require(database, "category", MoneyTables.CategorySignature);
        JetTable payees = MoneyTables.Require(database, "payee", MoneyTables.PayeeSignature);

        JetTable? splits = MoneyTables.Find(database, MoneyTables.SplitSignature);

        Dictionary<int, string> institutions = ReadInstitutions(database);
        IReadOnlyList<MoneyCategory> categoryList = ReadCategories(categories);
        Dictionary<int, (int Parent, int Index)> splitParts = ReadSplitParts(splits);

        var book = new MoneyBook
        {
            Accounts = ReadAccounts(accounts, institutions),
            Categories = categoryList,
            Payees = ReadPayees(payees),
            Transactions = ReadTransactions(transactions, splitParts),
            MerchantCodes = ReadMerchantCodes(database),
            Scheduled = ReadScheduled(database, transactions),
            Securities = ReadSecurities(database),
            Holdings = ReadHoldings(database),
            SecurityPrices = ReadSecurityPrices(database),
            Diagnostics = _diagnostics,
        };

        Summarise(book);
        return book;
    }

    /// <summary>
    /// Reads Money's recurring bills and deposits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The recurrence lives here; the payee, amount, account and category live on the template
    /// transaction this row points at through <c>lHtrn</c>. <b>Not <c>hbillHead</c></b> — that
    /// is a series identifier stamped on every generated instance rather than a transaction id,
    /// and following it as one resolves to an unrelated transaction for almost every bill.
    /// </para>
    /// <para>
    /// <c>cFrqInst</c> is read with <see cref="JetRow.Double"/> and not
    /// <see cref="JetRow.Int"/>. It is stored as a floating-point value, so the integer
    /// accessor returns null for every row — which would read as "no interval", collapse every
    /// twice-monthly series to monthly, and halve a salary without reporting anything at all.
    /// </para>
    /// </remarks>
    private IReadOnlyList<MoneyScheduled> ReadScheduled(JetDatabase database, JetTable transactions)
    {
        JetTable? table = MoneyTables.Find(database, MoneyTables.ScheduledSignature);

        if (table is null)
        {
            return [];
        }

        // The template rows are exactly the ones ReadTransactions skips — a row carrying a
        // frequency is a bill definition, not a register entry — so their details have to be
        // picked up here rather than looked up in the transactions that were kept.
        var templates = new Dictionary<int, (int Account, int? Payee, Money Amount, int? Category, string? Memo)>();

        foreach (JetRow row in transactions.Rows())
        {
            if (row.Int("htrn") is not int id || row.Int("hacct") is not int account)
            {
                continue;
            }

            if ((row.Int("frq") ?? NotRecurring) == NotRecurring)
            {
                continue;
            }

            templates[id] = (
                account,
                Positive(row.Int("lHpay")),
                ToMoney(row.Long("amt")),
                Positive(row.Int("hcat")),
                row.Text("mMemo"));
        }

        List<MoneyScheduled> series = new(table.DeclaredRowCount);
        int unmapped = 0;
        int orphaned = 0;

        foreach (JetRow row in table.Rows())
        {
            if (row.Int("hbill") is not int id
                || row.Int("lHtrn") is not int template
                || ToDate(row.Date("dt")) is not DateOnly due)
            {
                continue;
            }

            if (!templates.TryGetValue(template, out var detail))
            {
                // A bill whose template row is gone has no payee, amount or account, so there
                // is nothing to convert it into. Counted rather than guessed at.
                orphaned++;
                continue;
            }

            int? frq = row.Int("frq");
            double? count = row.Double("cFrqInst");
            MoneyFrequency.Mapping mapped = MoneyFrequency.Map(frq, count);

            if (!mapped.IsKnown)
            {
                unmapped++;
            }

            series.Add(new MoneyScheduled(
                Id: id,
                TemplateTransactionId: template,
                AccountId: detail.Account,
                PayeeId: detail.Payee,
                Amount: detail.Amount,
                CategoryId: detail.Category,
                Memo: detail.Memo,
                NextDue: due,
                Frequency: mapped.Frequency,
                EndDate: NeverEnds(row.Date("dtMax")) ? null : ToDate(row.Date("dtMax")),
                OccurrenceCount: row.Int("cInstMax") is int c and > 0 ? c : null,
                DaysAheadToEnter: row.Int("cDaysAutoEnter") is int d and > 0 ? d : null,
                RawFrequency: frq,
                RawCountPerPeriod: count,
                HeadId: Positive(row.Int("hbillHead")),
                Revision: row.Int("iinst") ?? 0));
        }

        // Money keeps one row per *revision* of a series, not one per series: when a bill's
        // amount or due date changes it writes a new row sharing the same hbillHead, with
        // iinst saying which instance the new terms take effect from. One real bill in this
        // file has six. Importing them all would create six active copies, five of them
        // anchored years in the past — each back-filling a decade of occurrences the moment
        // auto-entry ran. Only the newest revision of each series is the bill that exists now.
        int revisions = series.Count;

        series = [.. series
            .GroupBy(s => s.HeadId ?? -s.Id)
            .Select(g => g.OrderByDescending(s => s.Revision).ThenByDescending(s => s.NextDue).First())];

        if (revisions > series.Count)
        {
            _diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Info,
                "mny.bill.revisions",
                $"{revisions} recurring bill rows describe {series.Count} bills; the rest are "
                + "earlier versions of the same ones, kept by Money as their terms changed."));
        }

        if (orphaned > 0)
        {
            _diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                "mny.bill.orphan",
                $"{orphaned} recurring bills have no template row left in the file and cannot be "
                + "brought across."));
        }

        if (unmapped > 0)
        {
            _diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                "mny.bill.frequency",
                $"{unmapped} of {series.Count} recurring bills use a repeat pattern this version "
                + "has not been able to verify, and will be listed for you to set up by hand."));
        }

        return series;
    }

    /// <summary>
    /// Reads securities, positions and prices.
    /// </summary>
    /// <remarks>
    /// Quantity and prices only. <b>Money records no cost basis this reader can recover</b> —
    /// there is no quantity, price or cost column on its transaction table — so a migrated
    /// holding says what is owned and nothing about what it cost. Reporting that is better
    /// than importing a zero and letting it read as free.
    /// </remarks>
    private IReadOnlyList<MoneySecurity> ReadSecurities(JetDatabase database)
    {
        JetTable? table = MoneyTables.Find(database, MoneyTables.SecuritySignature);

        if (table is null)
        {
            return [];
        }

        var list = new List<MoneySecurity>(table.DeclaredRowCount);

        foreach (JetRow row in table.Rows())
        {
            if (row.Int("hsec") is int id and > 0 && row.Text("szFull") is string name && name.Length > 0)
            {
                list.Add(new MoneySecurity(id, name, row.Text("szSymbol")));
            }
        }

        return list;
    }

    private IReadOnlyList<MoneyHolding> ReadHoldings(JetDatabase database)
    {
        JetTable? table = MoneyTables.Find(database, MoneyTables.HoldingSignature);

        if (table is null)
        {
            return [];
        }

        var list = new List<MoneyHolding>(table.DeclaredRowCount);

        foreach (JetRow row in table.Rows())
        {
            if (row.Int("hacct") is not int account
                || account <= 0
                || row.Int("hsec") is not int security
                || security <= 0)
            {
                continue;
            }

            decimal quantity = (decimal)(row.Double("dQty") ?? 0);

            // A zero position is a security the account used to hold. Nothing to bring across.
            if (quantity != 0)
            {
                list.Add(new MoneyHolding(account, security, quantity));
            }
        }

        return list;
    }

    private IReadOnlyList<MoneySecurityPrice> ReadSecurityPrices(JetDatabase database)
    {
        JetTable? table = MoneyTables.Find(database, MoneyTables.SecurityPriceSignature);

        if (table is null)
        {
            return [];
        }

        var list = new List<MoneySecurityPrice>(table.DeclaredRowCount);

        foreach (JetRow row in table.Rows())
        {
            if (row.Int("hsec") is int security and > 0
                && ToDate(row.Date("dt")) is DateOnly asOf
                && row.Double("dPrice") is double price and > 0)
            {
                list.Add(new MoneySecurityPrice(security, asOf, (decimal)price));
            }
        }

        return list;
    }

    /// <summary>A Money id of zero or less means "not set".</summary>
    private static int? Positive(int? value) => value is int v and > 0 ? v : null;

    /// <summary>Money writes a far-future date rather than a null when a series never ends.</summary>
    private static bool NeverEnds(DateTime? value) => value is null || value.Value.Year >= 2200;

    /// <summary>Converts Money's 1/10,000 currency units to whole cents.</summary>
    /// <remarks>
    /// Through <see cref="decimal" /> rather than <see cref="double" />, so a figure that is
    /// exact in the file stays exact here. Rounding a hundredth of a cent the wrong way once
    /// is nothing; doing it nineteen thousand times is a balance that does not reconcile.
    /// </remarks>
    internal static Money ToMoney(long? currencyUnits) => currencyUnits is long units
        ? Money.FromMinorUnits((long)Math.Round(units / 100m, MidpointRounding.AwayFromZero))
        : Money.Zero;

    private IReadOnlyList<MoneyAccount> ReadAccounts(JetTable table, Dictionary<int, string> institutions)
    {
        var list = new List<MoneyAccount>(table.DeclaredRowCount);

        foreach (JetRow row in table.Rows())
        {
            int? id = row.Int("hacct");
            string? name = row.Text("szFull");

            if (id is null || name is null)
            {
                continue;
            }

            int typeCode = row.Int("at") ?? MoneyAccountTypeBank;
            (AccountType type, AccountGroup group) = MapAccountType(typeCode, name);

            list.Add(new MoneyAccount(
                Id: id.Value,
                Name: name,
                Type: type,
                Group: group,
                IsClosed: row.Bool("fClosed"),
                IsFavorite: row.Bool("fFavorite"),
                OpeningBalance: ToMoney(row.CurrencyUnits("amtOpen")),
                OpenedOn: ToDate(row.Date("dtOpen")),
                CreditLimit: row.CurrencyUnits("amtLimit") is long limit and not 0
                    ? ToMoney(limit)
                    : null,

                // Money holds the account number encrypted, and this application does not
                // store full numbers anyway. Nothing is lost by leaving it behind.
                AccountNumberMasked: null,
                Institution: row.Int("hfi") is int fi && institutions.TryGetValue(fi, out string? bank)
                    ? bank
                    : null));
        }

        return list;
    }

    /// <summary>
    /// Reads Money's account type, falling back on the account's own name.
    /// </summary>
    /// <remarks>
    /// Money records only that an account is a bank account; whether it is a current account,
    /// a savings account or a money market sits in the name and nowhere else. Reading the
    /// name is a guess, but it is a guess the user can see and correct, where filing
    /// everything as a current account would be a wrong answer they would never notice.
    /// </remarks>
    private static (AccountType Type, AccountGroup Group) MapAccountType(int code, string name) => code switch
    {
        MoneyAccountTypeCreditCard => (AccountType.CreditCard, AccountGroup.Credit),
        MoneyAccountTypeCash => (AccountType.Cash, AccountGroup.Bank),
        MoneyAccountTypeInvestment => (AccountType.UnsupportedImported, AccountGroup.Other),
        MoneyAccountTypeBank => (GuessBankType(name), AccountGroup.Bank),
        _ => (AccountType.UnsupportedImported, AccountGroup.Other),
    };

    private static AccountType GuessBankType(string name)
    {
        if (name.Contains("money market", StringComparison.OrdinalIgnoreCase))
        {
            return AccountType.MoneyMarket;
        }

        if (name.Contains("saving", StringComparison.OrdinalIgnoreCase))
        {
            return AccountType.Savings;
        }

        if (name.Contains("CD", StringComparison.Ordinal)
            || name.Contains("certificate", StringComparison.OrdinalIgnoreCase))
        {
            return AccountType.CertificateOfDeposit;
        }

        return AccountType.Checking;
    }

    /// <summary>
    /// Reads the category tree and works out which side of the books each branch is on.
    /// </summary>
    /// <remarks>
    /// Money's tree is three deep: two unnamed-in-the-UI roots, INCOME and EXPENSE, then
    /// headings, then subcategories. This application's is two deep with income and expense
    /// as a flag, so the roots become the flag and the remaining two levels line up exactly.
    /// </remarks>
    private IReadOnlyList<MoneyCategory> ReadCategories(JetTable table)
    {
        var raw = new Dictionary<int, (string Name, int? Parent, int Level)>();

        foreach (JetRow row in table.Rows())
        {
            int? id = row.Int("hcat");
            string? name = row.Text("szFull");

            if (id is null || name is null)
            {
                continue;
            }

            int? parent = row.Int("hcatParent");
            raw[id.Value] = (name, parent is > 0 ? parent : null, row.Int("nLevel") ?? 0);
        }

        var list = new List<MoneyCategory>(raw.Count);

        foreach ((int id, (string name, int? parent, int level)) in raw)
        {
            list.Add(new MoneyCategory(id, name, parent, level, KindOf(id, raw)));
        }

        return list;
    }

    /// <summary>Walks to the root to decide whether a category is income or expense.</summary>
    private static CategoryKind KindOf(int id, Dictionary<int, (string Name, int? Parent, int Level)> all)
    {
        int guard = 0;
        int current = id;

        while (guard++ < 16 && all.TryGetValue(current, out (string Name, int? Parent, int Level) node))
        {
            if (node.Parent is null)
            {
                return node.Name.Contains("income", StringComparison.OrdinalIgnoreCase)
                    ? CategoryKind.Income
                    : CategoryKind.Expense;
            }

            current = node.Parent.Value;
        }

        return CategoryKind.Expense;
    }

    private static IReadOnlyList<MoneyPayee> ReadPayees(JetTable table)
    {
        var list = new List<MoneyPayee>(table.DeclaredRowCount);

        foreach (JetRow row in table.Rows())
        {
            int? id = row.Int("hpay");
            string? name = row.Text("szFull");

            if (id is null || name is null)
            {
                continue;
            }

            list.Add(new MoneyPayee(id.Value, name, row.Bool("fHidden")));
        }

        return list;
    }

    private static Dictionary<int, (int Parent, int Index)> ReadSplitParts(JetTable? table)
    {
        var map = new Dictionary<int, (int, int)>();

        if (table is null)
        {
            return map;
        }

        foreach (JetRow row in table.Rows())
        {
            int? child = row.Int("htrn");
            int? parent = row.Int("htrnParent");

            if (child is not null && parent is not null)
            {
                map[child.Value] = (parent.Value, row.Int("iSplit") ?? 0);
            }
        }

        return map;
    }

    /// <summary>
    /// Reads Money's merchant-code table, if this file has one.
    /// </summary>
    /// <remarks>
    /// Optional: a book that never downloaded a transaction carrying an industry code will
    /// still have the table, but nothing is lost if it is absent.
    /// </remarks>
    private IReadOnlyList<MoneyMerchantCode> ReadMerchantCodes(JetDatabase database)
    {
        JetTable? table = MoneyTables.Find(database, MoneyTables.MerchantCodeSignature);

        if (table is null)
        {
            return [];
        }

        var codes = new List<MoneyMerchantCode>(table.DeclaredRowCount);

        foreach (JetRow row in table.Rows())
        {
            if (row.Int("sic") is int code and > 0 && row.Int("hcat") is int category and > 0)
            {
                codes.Add(new MoneyMerchantCode(
                    code.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    category));
            }
        }

        return codes;
    }

    private Dictionary<int, string> ReadInstitutions(JetDatabase database)
    {
        var map = new Dictionary<int, string>();
        JetTable? table = MoneyTables.Find(database, "hfi", "szBdsId", "szFull");

        if (table is null)
        {
            return map;
        }

        foreach (JetRow row in table.Rows())
        {
            if (row.Int("hfi") is int id && row.Text("szFull") is string name)
            {
                map[id] = name;
            }
        }

        return map;
    }

    private IReadOnlyList<MoneyTransaction> ReadTransactions(
        JetTable table,
        Dictionary<int, (int Parent, int Index)> splitParts)
    {
        var list = new List<MoneyTransaction>(table.DeclaredRowCount);
        int recurringDefinitions = 0;
        int undated = 0;

        foreach (JetRow row in table.Rows())
        {
            int? id = row.Int("htrn");
            int? account = row.Int("hacct");

            if (id is null || account is null)
            {
                continue;
            }

            // A row with a frequency is the definition of a recurring bill, not an entry in
            // the register. Money keeps both in one table; counting them as transactions
            // would add a phantom copy of every bill to the balance.
            if ((row.Int("frq") ?? NotRecurring) != NotRecurring)
            {
                recurringDefinitions++;
                continue;
            }

            if (ToDate(row.Date("dt")) is not DateOnly date)
            {
                undated++;
                continue;
            }

            int flags = row.Int("grftt") ?? 0;
            int scheduleFlags = row.Int("grfstem") ?? 0;
            ClearedStatus cleared = MapCleared(row.Int("cs") ?? 0);

            splitParts.TryGetValue(id.Value, out (int Parent, int Index) part);

            list.Add(new MoneyTransaction(
                Id: id.Value,
                AccountId: account.Value,
                LinkedAccountId: row.Int("hacctLink") is int link and > 0 ? link : null,
                Date: date,
                Amount: ToMoney(row.CurrencyUnits("amt")),
                CategoryId: row.Int("hcat") is int cat and > 0 ? cat : null,
                PayeeId: row.Int("lHpay") is int pay and > 0 ? pay : null,
                // Money stores a sort key here, not a number. See MoneyNumber.
                Number: MoneyNumber.Decode(row.Text("szId")),
                Memo: row.Text("mMemo"),
                Cleared: cleared,
                IsTransfer: (flags & TransferFlag) != 0,
                IsTransferSource: (flags & TransferSourceFlag) != 0,
                SplitParentId: part.Parent > 0 ? part.Parent : null,
                SplitIndex: part.Index,

                // Generated from a bill series and never reconciled: Money projected it into
                // the register but nobody ever entered it.
                IsScheduledInstance: (scheduleFlags & ScheduledFlag) != 0
                    && cleared == ClearedStatus.Uncleared,

                // Money's series key, stamped on every instance a bill generated. What lets
                // an already-entered occurrence be recognised exactly rather than guessed at.
                ScheduleHeadId: Positive(row.Int("hbillHead"))));
        }

        if (recurringDefinitions > 0)
        {
            _diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Info,
                "mny.recurring_definitions",
                $"{recurringDefinitions} recurring bill definitions were found. They are not transactions and were not counted as any."));
        }

        if (undated > 0)
        {
            _diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                "mny.undated_rows",
                $"{undated} transactions had no readable date and were skipped."));
        }

        return list;
    }

    /// <summary>
    /// Money records only "not reconciled" and "reconciled"; there is no middle state.
    /// </summary>
    private static ClearedStatus MapCleared(int code) => code switch
    {
        0 => ClearedStatus.Uncleared,
        1 => ClearedStatus.Cleared,
        _ => ClearedStatus.Reconciled,
    };

    private static DateOnly? ToDate(DateTime? value) =>
        value is DateTime when ? DateOnly.FromDateTime(when) : null;

    private void Summarise(MoneyBook book)
    {
        _diagnostics.Add(new ImportDiagnostic(
            ImportSeverity.Info,
            "mny.read",
            $"Read {book.Accounts.Count} accounts, {book.Categories.Count} categories, "
            + $"{book.Payees.Count} payees and {book.Transactions.Count} transactions."));

        int scheduled = book.Transactions.Count(t => t.IsScheduledInstance);

        if (scheduled > 0)
        {
            _diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                "mny.scheduled_instances",
                $"{scheduled} of those are bills Money wrote into the register but nobody ever entered. "
                + "Money counts them in its balances, so they are kept — otherwise the migrated balances would not match."));
        }

        if (book.MerchantCodes.Count > 0)
        {
            _diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Info,
                "mny.merchant_codes",
                $"{book.MerchantCodes.Count} merchant industry codes were found and will be brought across. "
                + "They help categorize a shop you have never used before, when your bank sends the code."));
        }

        int unsupported = book.Accounts.Count(a => a.Type == AccountType.UnsupportedImported);

        if (unsupported > 0)
        {
            _diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                "mny.unsupported_accounts",
                $"{unsupported} accounts are investment or loan accounts, which this application does not model. "
                + "Their transactions come across but their holdings do not."));
        }
    }
}
