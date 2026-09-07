using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Import.Mny;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// Locates a real Money file, when the developer happens to have one.
/// </summary>
/// <remarks>
/// A `.mny` holds somebody's entire financial history, is gitignored and is never committed.
/// These tests therefore assert only what stays true of any book — never a balance or an
/// account name, which would put the file's contents into source control by the back door.
/// </remarks>
internal static class MoneyFile
{
    private static readonly Lazy<string?> Located = new(Locate);

    public static string? Path => Located.Value;

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            FileInfo[] found = directory.GetFiles("*.mny", SearchOption.TopDirectoryOnly);

            if (found.Length > 0)
            {
                return found[0].FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>A test that only runs when a real Money file is on the machine.</summary>
public sealed class MoneyFileFactAttribute : FactAttribute
{
    public MoneyFileFactAttribute()
    {
        if (MoneyFile.Path is null)
        {
            Skip = "No .mny file was found. Put one in the repository root to run this test.";
        }
    }
}

/// <summary>
/// Migrates a real Money file into a real encrypted book, end to end.
/// </summary>
/// <remarks>
/// This is the test that matters: everything else checks one rule at a time against a book
/// assembled in code, where the shapes are the ones expected. A twenty-five year old file
/// contains shapes nobody expected.
/// </remarks>
public sealed class MoneyFileMigrationTests
{
    [MoneyFileFact]
    public async Task A_real_money_file_migrates_and_every_balance_survives_the_journey()
    {
        using var harness = new BookHarness();

        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        var clock = Stopwatch.StartNew();
        MigrationSummary summary = await harness.Migration.MigrateAsync(book);
        clock.Stop();

        summary.AccountsCreated.ShouldBe(book.Accounts.Count);
        summary.TransactionsCreated.ShouldBe(book.TopLevelTransactions.Count());

        await using MyFinanceDbContext db = harness.CreateContext();

        // The figure Money would show for each account, recomputed from what was written.
        foreach (MigrationAccountPreview expected in summary.Accounts)
        {
            Account account = await db.Accounts.SingleAsync(a => a.Name == expected.Name);

            Money written = account.OpeningBalance + Money.Sum(
                await db.Transactions
                    .Where(t => t.AccountId == account.Id)
                    .Select(t => t.Amount)
                    .ToListAsync());

            written.ShouldBe(expected.Balance);
        }

        // A migration nobody will sit through is one nobody will run.
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// Every transaction must carry at least one split summing to its amount. This is the
    /// invariant the register, the reports and the budget all lean on.
    /// </summary>
    [MoneyFileFact]
    public async Task Every_migrated_transaction_has_splits_that_add_up_to_it()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(MoneyReader.Read(MoneyFile.Path!));

        await using MyFinanceDbContext db = harness.CreateContext();

        // Money is a value object, so the comparison happens here rather than in SQL.
        List<Transaction> all = await db.Transactions.Include(t => t.Splits).ToListAsync();

        all.ShouldNotBeEmpty();

        List<int> wrong =
        [
            .. all.Where(t => t.Splits.Count == 0
                    || Money.Sum(t.Splits.Select(s => s.Amount)) != t.Amount)
                .Select(t => t.Id),
        ];

        wrong.ShouldBeEmpty();
    }

    [MoneyFileFact]
    public async Task Both_legs_of_every_migrated_transfer_point_at_each_other()
    {
        using var harness = new BookHarness();
        MigrationSummary summary = await harness.Migration.MigrateAsync(
            MoneyReader.Read(MoneyFile.Path!));

        summary.TransfersLinked.ShouldBeGreaterThan(0);

        await using MyFinanceDbContext db = harness.CreateContext();

        List<Transaction> legs = await db.Transactions
            .Where(t => t.TransferPeerId != null)
            .ToListAsync();

        Dictionary<int, Transaction> byId = legs.ToDictionary(t => t.Id);

        foreach (Transaction leg in legs)
        {
            byId.ShouldContainKey(leg.TransferPeerId!.Value);
            byId[leg.TransferPeerId.Value].TransferPeerId.ShouldBe(leg.Id);
        }
    }

    // -- Recurring bills ----------------------------------------------------------------

    [MoneyFileFact]
    public async Task Recurring_bills_come_across_as_scheduled_transactions()
    {
        using var harness = new BookHarness();
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        summary.BillsCreated.ShouldBeGreaterThan(0);

        await using MyFinanceDbContext db = harness.CreateContext();
        (await db.ScheduledTransactions.CountAsync()).ShouldBe(summary.BillsCreated);
    }

    [MoneyFileFact]
    public async Task Every_converted_bill_has_a_frequency_that_was_verified_not_guessed()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(MoneyReader.Read(MoneyFile.Path!));

        await using MyFinanceDbContext db = harness.CreateContext();

        // Only the three patterns established against Money's own Bills screen. Anything else
        // would mean a code was mapped on a hunch.
        RecurrenceFrequency[] verified =
        [
            RecurrenceFrequency.Monthly,
            RecurrenceFrequency.TwiceAMonth,
            RecurrenceFrequency.Quarterly,
        ];

        List<RecurrenceFrequency> used = await db.ScheduledTransactions
            .Select(s => s.Frequency)
            .Distinct()
            .ToListAsync();

        used.ShouldBeSubsetOf(verified);
    }

    [MoneyFileFact]
    public async Task A_twice_monthly_series_is_never_turned_into_a_two_monthly_one()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(MoneyReader.Read(MoneyFile.Path!));

        await using MyFinanceDbContext db = harness.CreateContext();

        // Money's count-per-period is not our interval. Carried through, cFrqInst=2 would
        // become "every two months" and halve the largest inflow in the book.
        List<ScheduledTransaction> twiceMonthly = await db.ScheduledTransactions
            .Where(s => s.Frequency == RecurrenceFrequency.TwiceAMonth)
            .ToListAsync();

        twiceMonthly.ShouldNotBeEmpty();
        twiceMonthly.ShouldAllBe(s => s.Interval == 1);
    }

    [MoneyFileFact]
    public async Task Every_converted_bill_points_at_an_account_that_can_hold_one()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(MoneyReader.Read(MoneyFile.Path!));

        await using MyFinanceDbContext db = harness.CreateContext();

        List<ScheduledTransaction> bills = await db.ScheduledTransactions
            .Include(s => s.Account)
            .ToListAsync();

        bills.ShouldAllBe(s => s.Account!.Type != AccountType.UnsupportedImported);
    }

    [MoneyFileFact]
    public async Task A_migrated_bill_does_not_re_enter_occurrences_already_in_the_register()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(MoneyReader.Read(MoneyFile.Path!));

        await using MyFinanceDbContext db = harness.CreateContext();

        // Money projects bills forward into its own register and 009 keeps those rows, so a
        // freshly migrated book already holds transactions for occurrences the new schedule
        // would otherwise consider outstanding. Without the entered markers, the first
        // auto-entry after a migration pays every one of them a second time.
        (await db.ScheduleOccurrences.CountAsync(o => o.State == ScheduleOccurrenceState.Entered))
            .ShouldBeGreaterThan(0);
    }

    [MoneyFileFact]
    public async Task Auto_entry_on_a_freshly_migrated_book_never_pays_a_backdated_bill_again()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(MoneyReader.Read(MoneyFile.Path!));

        DateOnly settled;
        HashSet<int> before;

        await using (MyFinanceDbContext db = harness.CreateContext())
        {
            // The newest thing the migrated book already contains. Everything up to here was
            // dealt with in Money and must never be entered a second time.
            settled = await db.Transactions.MaxAsync(t => t.Date);
            before = await db.Transactions.Select(t => t.Id).ToHashSetAsync();
        }

        await harness.Schedules.AutoEnterDueAsync(settled.AddDays(30));

        await using MyFinanceDbContext after = harness.CreateContext();

        List<DateOnly> added = await after.Transactions
            .Where(t => !before.Contains(t.Id))
            .Select(t => t.Date)
            .ToListAsync();

        // Anything auto-entry adds must be genuinely new — dated after the point Money had
        // already reached. A single backdated row here is a bill paid twice.
        added.ShouldAllBe(d => d > settled);
    }

    [MoneyFileFact]
    public async Task Running_auto_entry_twice_on_a_migrated_book_adds_nothing_the_second_time()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(MoneyReader.Read(MoneyFile.Path!));

        DateOnly settled;
        await using (MyFinanceDbContext db = harness.CreateContext())
        {
            settled = await db.Transactions.MaxAsync(t => t.Date);
        }

        await harness.Schedules.AutoEnterDueAsync(settled.AddDays(30));

        int afterFirst;
        await using (MyFinanceDbContext db = harness.CreateContext())
        {
            afterFirst = await db.Transactions.CountAsync();
        }

        await harness.Schedules.AutoEnterDueAsync(settled.AddDays(30));

        await using MyFinanceDbContext final = harness.CreateContext();
        (await final.Transactions.CountAsync()).ShouldBe(afterFirst);
    }

    [MoneyFileFact]
    public async Task Every_bill_is_either_converted_or_listed_for_manual_setup()
    {
        using var harness = new BookHarness();
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        // The user's manual list must be complete: nothing may be quietly dropped between the
        // file and the report.
        (summary.BillsCreated + summary.UnconvertedBills.Count).ShouldBe(book.Scheduled.Count);

        summary.UnconvertedBills.ShouldAllBe(b => !string.IsNullOrWhiteSpace(b.Reason));
    }

    // -- Investments --------------------------------------------------------------------

    [MoneyFileFact]
    public async Task Holdings_come_across_with_their_quantity()
    {
        using var harness = new BookHarness();
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();

        (await db.Holdings.CountAsync()).ShouldBe(summary.HoldingsCreated);
        (await db.Securities.CountAsync()).ShouldBe(book.Securities.Count);

        foreach (Holding holding in await db.Holdings.ToListAsync())
        {
            holding.Quantity.ShouldNotBe(Quantity.Zero);
        }
    }

    [MoneyFileFact]
    public async Task A_migrated_holding_says_that_its_cost_is_unknown_rather_than_zero()
    {
        using var harness = new BookHarness();
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        if (summary.HoldingsCreated == 0)
        {
            return;
        }

        // Money records no cost basis this reader can recover. A zero left unexplained would
        // read as though the shares had been free — so the summary says so in as many words.
        summary.Diagnostics.ShouldContain(d => d.Code == "mny.holding.no_cost");
    }

    [MoneyFileFact]
    public async Task Recorded_prices_come_across_with_their_dates()
    {
        using var harness = new BookHarness();
        MoneyBook book = MoneyReader.Read(MoneyFile.Path!);

        await harness.Migration.MigrateAsync(book);

        await using MyFinanceDbContext db = harness.CreateContext();

        (await db.SecurityPrices.CountAsync()).ShouldBe(book.SecurityPrices.Count);

        foreach (SecurityPrice price in await db.SecurityPrices.ToListAsync())
        {
            price.Price.IsPositive.ShouldBeTrue();
        }
    }
}
