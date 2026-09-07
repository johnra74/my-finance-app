using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Mny;
using MyFinance.Import.Mny.Jet;

namespace MyFinance.Import.Tests.Mny;

/// <summary>
/// Reads a real Money file and checks what must be true of any book.
/// </summary>
/// <remarks>
/// Every assertion here is structural. None of them names an account, a payee or a figure,
/// because the file under test is the developer's own and these assertions are committed.
/// The checks are still sharp: a reader that mis-locates a column or drops rows fails
/// several of them at once.
/// </remarks>
public sealed class MoneyReaderTests
{
    private static JetDatabase Open() => JetDatabase.Open(MoneyFile.Path!);

    [MoneyFileFact]
    public void The_file_opens_and_declares_tables()
    {
        JetDatabase database = Open();

        database.PageSize.ShouldBe(4096);
        database.PageCount.ShouldBeGreaterThan(16);
        database.Tables.Count.ShouldBeGreaterThan(20);
    }

    /// <summary>
    /// Each table definition states how many rows it has. Reading exactly that many is the
    /// single strongest check available: a row walker that loses a page, mistakes a deleted
    /// row for a live one or misreads a row length will not land on the number by accident.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every table the migration reads must be exact.</b> That is asserted without
    /// exception, because a table that reads short there is silent data loss — a transaction
    /// that never arrives, in a migration nobody can check afterwards.
    /// </para>
    /// <para>
    /// A shortfall elsewhere is reported rather than ignored, but does not fail: Money keeps
    /// tables this application never opens, and at least one of them (its advice and alerts
    /// table) reads one row short on a real file. The cause is a row stored on an overflow
    /// page, which <see cref="JetTable.Rows"/> skips along with deleted rows rather than
    /// following. That is a genuine reader defect and it is recorded as one — see
    /// `specs/009-money-migration`. It has no effect on any feature, and fixing it is Jet
    /// work in its own right rather than something to fold into an unrelated change.
    /// </para>
    /// </remarks>
    [MoneyFileFact]
    public void Every_table_the_migration_reads_yields_exactly_the_row_count_it_declares()
    {
        JetDatabase database = Open();

        JetTable?[] used =
        [
            MoneyTables.Find(database, MoneyTables.AccountSignature),
            MoneyTables.Find(database, MoneyTables.TransactionSignature),
            MoneyTables.Find(database, MoneyTables.CategorySignature),
            MoneyTables.Find(database, MoneyTables.PayeeSignature),
            MoneyTables.Find(database, MoneyTables.SplitSignature),
            MoneyTables.Find(database, MoneyTables.MerchantCodeSignature),
            MoneyTables.Find(database, MoneyTables.ScheduledSignature),
        ];

        var wrong = new List<string>();

        foreach (JetTable table in used.OfType<JetTable>())
        {
            int read = table.Rows().Count();

            if (read != table.DeclaredRowCount)
            {
                wrong.Add($"page {table.DefinitionPage}: declared {table.DeclaredRowCount}, read {read}");
            }
        }

        wrong.ShouldBeEmpty();
    }

    /// <summary>
    /// The same check across every table in the file, including the many this application
    /// never opens.
    /// </summary>
    /// <remarks>
    /// Kept separate and deliberately tolerant of the one known shortfall, so that a *new*
    /// short read anywhere still shows up. Deleting the check because one table fails it would
    /// throw away the canary that found the defect in the first place.
    /// </remarks>
    [MoneyFileFact]
    public void No_table_reads_short_except_the_one_known_overflow_row()
    {
        JetDatabase database = Open();
        var wrong = new List<string>();

        foreach (JetTable table in database.Tables)
        {
            int read = table.Rows().Count();
            int shortfall = table.DeclaredRowCount - read;

            // The known defect: a single row stored on an overflow page, on a table nothing
            // reads. Anything else — any other table, or a larger shortfall — is new.
            bool known = shortfall == 1 && !IsUsedByMigration(database, table);

            if (read != table.DeclaredRowCount && !known)
            {
                wrong.Add($"page {table.DefinitionPage}: declared {table.DeclaredRowCount}, read {read}");
            }
        }

        wrong.ShouldBeEmpty();
    }

    private static bool IsUsedByMigration(JetDatabase database, JetTable table) =>
        new[]
        {
            MoneyTables.AccountSignature,
            MoneyTables.TransactionSignature,
            MoneyTables.CategorySignature,
            MoneyTables.PayeeSignature,
            MoneyTables.SplitSignature,
            MoneyTables.MerchantCodeSignature,
            MoneyTables.ScheduledSignature,
        }.Any(sig => MoneyTables.Find(database, sig)?.DefinitionPage == table.DefinitionPage);

    [MoneyFileFact]
    public void The_core_tables_are_all_identifiable_by_their_columns()
    {
        JetDatabase database = Open();

        MoneyTables.Find(database, MoneyTables.AccountSignature).ShouldNotBeNull();
        MoneyTables.Find(database, MoneyTables.TransactionSignature).ShouldNotBeNull();
        MoneyTables.Find(database, MoneyTables.CategorySignature).ShouldNotBeNull();
        MoneyTables.Find(database, MoneyTables.PayeeSignature).ShouldNotBeNull();
    }

    [MoneyFileFact]
    public void A_book_reads_with_accounts_categories_payees_and_transactions()
    {
        MoneyBook book = MoneyReader.Read(Open());

        book.Accounts.ShouldNotBeEmpty();
        book.Categories.ShouldNotBeEmpty();
        book.Payees.ShouldNotBeEmpty();
        book.Transactions.ShouldNotBeEmpty();

        book.Accounts.ShouldAllBe(a => a.Name.Length > 0);
        book.Categories.ShouldAllBe(c => c.Name.Length > 0);
        book.Payees.ShouldAllBe(p => p.Name.Length > 0);
    }

    /// <summary>Text that decoded wrongly shows up as control characters or CJK.</summary>
    [MoneyFileFact]
    public void Names_decode_as_readable_text()
    {
        MoneyBook book = MoneyReader.Read(Open());

        book.Accounts.ShouldAllBe(a => a.Name.All(c => !char.IsControl(c)));

        // A book kept in English should be overwhelmingly ASCII. A misread column would
        // produce CJK code points for nearly every row, not just a stray accent.
        int ascii = book.Payees.Count(p => p.Name.All(char.IsAscii));
        ascii.ShouldBeGreaterThan(book.Payees.Count / 2);
    }

    [MoneyFileFact]
    public void Every_transaction_belongs_to_an_account_that_exists()
    {
        MoneyBook book = MoneyReader.Read(Open());
        HashSet<int> accounts = [.. book.Accounts.Select(a => a.Id)];

        book.Transactions.ShouldAllBe(t => accounts.Contains(t.AccountId));
    }

    [MoneyFileFact]
    public void Dates_land_inside_the_years_a_person_could_have_kept_books()
    {
        MoneyBook book = MoneyReader.Read(Open());

        book.Transactions.ShouldAllBe(t => t.Date.Year >= 1980 && t.Date.Year <= 2100);
    }

    /// <summary>
    /// A split's parts must add up to the transaction they belong to. This catches a
    /// currency column read at the wrong offset better than any single value could.
    /// </summary>
    [MoneyFileFact]
    public void The_parts_of_a_split_add_up_to_their_parent()
    {
        MoneyBook book = MoneyReader.Read(Open());
        Dictionary<int, MoneyTransaction> byId = book.Transactions.ToDictionary(t => t.Id);

        var mismatched = new List<int>();

        foreach (IGrouping<int, MoneyTransaction> split in book.SplitPartsByParent)
        {
            if (!byId.TryGetValue(split.Key, out MoneyTransaction? parent))
            {
                continue;
            }

            if (Money.Sum(split.Select(p => p.Amount)) != parent.Amount)
            {
                mismatched.Add(split.Key);
            }
        }

        mismatched.ShouldBeEmpty();
    }

    /// <summary>
    /// Money writes a transfer as two rows that negate each other, which is the same
    /// convention this application uses. Every leg should name the account at the far end.
    /// </summary>
    [MoneyFileFact]
    public void Every_transfer_leg_names_the_account_at_the_other_end()
    {
        MoneyBook book = MoneyReader.Read(Open());
        HashSet<int> accounts = [.. book.Accounts.Select(a => a.Id)];

        List<MoneyTransaction> legs = [.. book.Transactions.Where(t => t.IsTransfer)];

        legs.ShouldNotBeEmpty();

        foreach (MoneyTransaction leg in legs)
        {
            leg.LinkedAccountId.ShouldNotBeNull();
            leg.LinkedAccountId.ShouldNotBe(leg.AccountId);
            accounts.ShouldContain(leg.LinkedAccountId!.Value);
        }
    }

    /// <summary>
    /// The two legs of a transfer pair off exactly: same day, equal and opposite amounts,
    /// each naming the other's account.
    /// </summary>
    /// <remarks>
    /// A leg can itself be one line of a split — the book under test has one such transfer
    /// from 2005 — so split parts are counted here rather than filtered out. Leaving them
    /// out breaks precisely that one pair, which is a fact about the data and not about the
    /// reader.
    /// </remarks>
    [MoneyFileFact]
    public void The_two_legs_of_every_transfer_cancel_each_other()
    {
        MoneyBook book = MoneyReader.Read(Open());

        var destinations = new Dictionary<(DateOnly, long, int, int), int>();

        foreach (MoneyTransaction leg in book.Transactions.Where(t => t.IsTransfer && !t.IsTransferSource))
        {
            var key = (leg.Date, leg.Amount.Abs().MinorUnits, leg.AccountId, leg.LinkedAccountId ?? 0);
            destinations[key] = destinations.GetValueOrDefault(key) + 1;
        }

        var unpaired = new List<int>();

        foreach (MoneyTransaction leg in book.Transactions.Where(t => t.IsTransfer && t.IsTransferSource))
        {
            var key = (leg.Date, leg.Amount.Abs().MinorUnits, leg.LinkedAccountId ?? 0, leg.AccountId);

            if (destinations.GetValueOrDefault(key) > 0)
            {
                destinations[key]--;
            }
            else
            {
                unpaired.Add(leg.Id);
            }
        }

        unpaired.ShouldBeEmpty();
        destinations.Values.Sum().ShouldBe(0);
    }

    [MoneyFileFact]
    public void The_category_tree_has_no_cycles_and_every_parent_exists()
    {
        MoneyBook book = MoneyReader.Read(Open());
        Dictionary<int, MoneyCategory> byId = book.Categories.ToDictionary(c => c.Id);

        foreach (MoneyCategory category in book.Categories)
        {
            int guard = 0;
            MoneyCategory current = category;

            while (current.ParentId is int parent)
            {
                byId.ShouldContainKey(parent);
                current = byId[parent];
                (++guard).ShouldBeLessThan(8);
            }
        }
    }

    /// <summary>
    /// Money's two roots are income and expense, so both kinds must appear. A book with
    /// everything filed as expense would mean the root walk is broken.
    /// </summary>
    [MoneyFileFact]
    public void Both_sides_of_the_category_tree_are_represented()
    {
        MoneyBook book = MoneyReader.Read(Open());

        book.Categories.ShouldContain(c => c.Kind == CategoryKind.Income);
        book.Categories.ShouldContain(c => c.Kind == CategoryKind.Expense);
    }

    [MoneyFileFact]
    public void Reading_the_same_file_twice_gives_the_same_answer()
    {
        MoneyBook first = MoneyReader.Read(Open());
        MoneyBook second = MoneyReader.Read(Open());

        second.Accounts.Count.ShouldBe(first.Accounts.Count);
        second.Transactions.Count.ShouldBe(first.Transactions.Count);

        Money.Sum(second.Transactions.Select(t => t.Amount))
            .ShouldBe(Money.Sum(first.Transactions.Select(t => t.Amount)));
    }

    // -- Recurring bills ----------------------------------------------------------------

    [MoneyFileFact]
    public void Recurring_bill_definitions_are_read_with_their_amount_and_account()
    {
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        book.Scheduled.ShouldNotBeEmpty();

        foreach (MoneyScheduled series in book.Scheduled)
        {
            // Every series must resolve to a real account and carry a usable anchor date,
            // or there is nothing to convert it into.
            book.Accounts.ShouldContain(a => a.Id == series.AccountId);
            series.NextDue.Year.ShouldBeInRange(1980, 2100);

            if (series.PayeeId is int payee)
            {
                book.Payees.ShouldContain(p => p.Id == payee);
            }
        }
    }

    [MoneyFileFact]
    public void A_recurring_bill_is_never_also_counted_as_a_transaction()
    {
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        // Money keeps bill definitions in the same table as register entries. Counting them
        // as transactions would add a phantom copy of every bill to the balance.
        var templates = book.Scheduled.Select(s => s.TemplateTransactionId).ToHashSet();

        book.Transactions.ShouldNotContain(t => templates.Contains(t.Id));
    }

    [MoneyFileFact]
    public void Every_recurring_bill_carries_a_repeat_pattern_or_says_it_could_not()
    {
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        // Whichever way each one falls, it must be one or the other — never a silent default.
        foreach (MoneyScheduled series in book.Scheduled)
        {
            if (series.IsConvertible)
            {
                series.Frequency.ShouldNotBeNull();
            }
            else
            {
                series.Frequency.ShouldBeNull();
                series.RawFrequency.ShouldNotBeNull("an unmapped series must still report its code");
            }
        }
    }

    [MoneyFileFact]
    public void A_series_that_never_ends_has_no_end_date()
    {
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        // Money writes 2200-12-31 rather than a null. Carried through literally, every bill
        // would appear to end on the same day 175 years from now.
        book.Scheduled.Any(s => s.EndDate?.Year >= 2200).ShouldBeFalse();
        book.Scheduled.Any(s => s.EndDate is null).ShouldBeTrue();
    }

    // -- Investments --------------------------------------------------------------------

    [MoneyFileFact]
    public void Securities_holdings_and_prices_are_read_when_the_file_has_them()
    {
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        // A book with no investments reads three empty lists rather than failing.
        book.Securities.ShouldNotBeNull();
        book.Holdings.ShouldNotBeNull();
        book.SecurityPrices.ShouldNotBeNull();

        foreach (MoneyHolding holding in book.Holdings)
        {
            // A position must name a security that exists, or there is nothing to hold.
            book.Securities.ShouldContain(s => s.Id == holding.SecurityId);

            // A zero position is a security the account used to hold; it is not brought across.
            holding.Quantity.ShouldNotBe(0m);
        }

        foreach (MoneySecurityPrice price in book.SecurityPrices)
        {
            book.Securities.ShouldContain(s => s.Id == price.SecurityId);
            price.Price.ShouldBeGreaterThan(0m);
            price.AsOf.Year.ShouldBeInRange(1980, 2100);
        }
    }

    [MoneyFileFact]
    public void Every_security_has_a_name()
    {
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        book.Securities.ShouldAllBe(s => !string.IsNullOrWhiteSpace(s.Name));
    }

    /// <summary>
    /// No transaction number still carries Money's sort flag.
    /// </summary>
    /// <remarks>
    /// A shape, never a value: this asserts that nothing looks like <c>"0" + padding + digits</c>
    /// or a flag followed by unpadded text, and prints nothing if it fails beyond a count.
    /// The reader used to hand these straight to the register — see MoneyNumberTests.
    /// </remarks>
    [MoneyFileFact]
    public void No_transaction_number_still_carries_the_sort_flag()
    {
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        string?[] numbers =
        [
            .. book.Transactions
                .Select(t => t.Number)
                .Where(n => !string.IsNullOrEmpty(n))
        ];

        // The file is known to use the field, so a pass here means the decoding ran rather
        // than that there was nothing to decode.
        numbers.Length.ShouldBeGreaterThan(0);

        numbers.Count(n => n!.Length == 13 && n[0] == '0' && n[1] == ' ')
            .ShouldBe(0, "a padded cheque number reached the register with its flag on");

        numbers.Count(n => n!.Length > 1 && n[0] == '1' && !n.Skip(1).Any(char.IsDigit))
            .ShouldBe(0, "a text reference reached the register with its flag on");

        numbers.ShouldAllBe(n => n == n!.Trim());
    }
}
