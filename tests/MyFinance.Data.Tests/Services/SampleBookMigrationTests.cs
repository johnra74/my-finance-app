using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Data.Tests.Fixtures;
using MyFinance.Import.Mny;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// The whole-book migration invariants, run against an invented book.
/// </summary>
/// <remarks>
/// <para>
/// These are the same checks <see cref="MoneyFileMigrationTests"/> makes against a real
/// <c>.mny</c>, which skip on every machine that does not have one — which is most of them,
/// because the file is gitignored and can never be committed. Running them over
/// <see cref="SampleBook"/> as well means the arithmetic that matters is checked everywhere,
/// and the real-file tests are left to do the one thing only they can: meet shapes nobody
/// invented.
/// </para>
/// </remarks>
public sealed class SampleBookMigrationTests
{
    [Fact]
    public async Task The_sample_book_migrates_and_every_balance_survives_the_journey()
    {
        using var harness = new BookHarness();
        MoneyBook book = SampleBook.Create();

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        summary.AccountsCreated.ShouldBe(book.Accounts.Count);

        await using MyFinanceDbContext db = harness.CreateContext();

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
    }

    [Fact]
    public async Task Every_migrated_transaction_has_splits_that_add_up_to_it()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(SampleBook.Create());

        await using MyFinanceDbContext db = harness.CreateContext();

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

    [Fact]
    public async Task Both_legs_of_every_migrated_transfer_point_at_each_other()
    {
        using var harness = new BookHarness();
        MigrationSummary summary = await harness.Migration.MigrateAsync(SampleBook.Create());

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

    /// <summary>
    /// A split has to arrive as a split, not as a lump with the breakdown thrown away.
    /// </summary>
    [Fact]
    public async Task A_split_transaction_keeps_its_parts()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(SampleBook.Create());

        await using MyFinanceDbContext db = harness.CreateContext();

        List<Transaction> split = await db.Transactions
            .Include(t => t.Splits)
            .Where(t => t.Splits.Count > 1)
            .ToListAsync();

        split.ShouldNotBeEmpty();
        split.ShouldAllBe(t => t.Splits.Count == 2);
    }

    /// <summary>
    /// A bill whose repeat code has never been verified must be reported, not guessed at.
    /// </summary>
    [Fact]
    public async Task Only_verified_recurrences_are_converted_and_the_rest_are_reported()
    {
        using var harness = new BookHarness();
        MoneyBook book = SampleBook.Create();

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        int convertible = book.Scheduled.Count(s => s.IsConvertible);

        summary.BillsCreated.ShouldBe(convertible);
        summary.UnconvertedBills.Count.ShouldBe(book.Scheduled.Count - convertible);

        await using MyFinanceDbContext db = harness.CreateContext();

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

    /// <summary>
    /// The backlog rule: a due date Money actually generated arrives entered, and one it only
    /// ever showed as overdue arrives skipped — never as a payment nobody made.
    /// </summary>
    [Fact]
    public async Task A_migrated_bill_settles_the_occurrences_the_book_already_covers()
    {
        using var harness = new BookHarness();
        await harness.Migration.MigrateAsync(SampleBook.Create());

        await using MyFinanceDbContext db = harness.CreateContext();

        List<ScheduleOccurrence> occurrences = await db.ScheduleOccurrences.ToListAsync();

        occurrences.ShouldNotBeEmpty();
        occurrences.ShouldContain(o => o.State == ScheduleOccurrenceState.Entered);
        occurrences.ShouldAllBe(o =>
            o.State == ScheduleOccurrenceState.Entered
            || o.State == ScheduleOccurrenceState.Skipped);
        // Nothing beyond where the book itself stops: every due date after that is still the
        // schedule's to raise, and settling it here would decide it on the user's behalf.
        DateOnly cutoff = SampleBook.Create().Transactions.Max(t => t.Date);
        occurrences.ShouldAllBe(o => o.DueDate <= cutoff);
    }

    /// <summary>
    /// Instances Money projected but nobody entered come across by default, because leaving
    /// them out makes the migrated balances disagree with Money's own account list — and can
    /// be left out on request, which gives a tidier book that will not reconcile.
    /// </summary>
    [Fact]
    public async Task Projected_instances_come_across_by_default_and_can_be_left_out()
    {
        MoneyBook book = SampleBook.Create();

        int total = book.TopLevelTransactions.Count();
        int projected = book.TopLevelTransactions.Count(t => t.IsScheduledInstance);

        projected.ShouldBeGreaterThan(0);

        using (var kept = new BookHarness())
        {
            MigrationSummary summary = await kept.Migration.MigrateAsync(book);

            summary.TransactionsCreated.ShouldBe(total);

            await using MyFinanceDbContext db = kept.CreateContext();
            (await db.Transactions.CountAsync()).ShouldBe(total);
        }

        using var dropped = new BookHarness();

        MigrationSummary tidied = await dropped.Migration.MigrateAsync(
            book,
            new MigrationOptions { IncludeScheduledInstances = false });

        tidied.TransactionsCreated.ShouldBe(total - projected);

        await using MyFinanceDbContext tidyDb = dropped.CreateContext();
        (await tidyDb.Transactions.CountAsync()).ShouldBe(total - projected);
    }

    [Fact]
    public async Task Holdings_and_their_securities_come_across()
    {
        using var harness = new BookHarness();
        MoneyBook book = SampleBook.Create();

        MigrationSummary summary = await harness.Migration.MigrateAsync(book);

        summary.HoldingsCreated.ShouldBe(book.Holdings.Count);

        await using MyFinanceDbContext db = harness.CreateContext();
        (await db.Securities.CountAsync()).ShouldBe(book.Securities.Count);
    }

    /// <summary>
    /// The fixture is generated, so it is worth proving it is generated the same way twice —
    /// a test that fails only on some runs is worse than no test at all.
    /// </summary>
    [Fact]
    public void The_sample_book_is_the_same_book_every_time()
    {
        MoneyBook first = SampleBook.Create();
        MoneyBook second = SampleBook.Create();

        first.Transactions.Count.ShouldBe(second.Transactions.Count);
        first.Transactions.ShouldBe(second.Transactions);
        first.BalanceOf(SampleBook.Checking).ShouldBe(second.BalanceOf(SampleBook.Checking));
    }
}
