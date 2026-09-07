using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Progress;
using MyFinance.Data.Security;
using MyFinance.Data.Services;
using MyFinance.Import.Mny;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// That slow work says how it is going, and that stopping it leaves nothing behind.
/// </summary>
/// <remarks>
/// The window freezing during a migration could not be reproduced in a test — there is no UI
/// thread here to block. What can be checked is the thing that made it invisible: whether the
/// work reports anything at all while it runs. An operation that reports once at the end has
/// nothing for a progress bar to draw, which is exactly the state this replaces.
/// </remarks>
public sealed class ProgressAndCancellationTests
{
    private sealed class Recorder : IProgress<WorkProgress>
    {
        private readonly List<WorkProgress> _reports = [];

        public IReadOnlyList<WorkProgress> Reports
        {
            get
            {
                lock (_reports)
                {
                    return [.. _reports];
                }
            }
        }

        public void Report(WorkProgress value)
        {
            lock (_reports)
            {
                _reports.Add(value);
            }
        }
    }

    private static MoneyBook BookOf(int transactions)
    {
        var account = new MoneyAccount(
            1, "Checking", AccountType.Checking, AccountGroup.Bank, false, false,
            Money.Zero, null, null, null, null);

        var rows = new List<MoneyTransaction>(transactions);

        for (int i = 0; i < transactions; i++)
        {
            rows.Add(new MoneyTransaction(
                Id: i + 1,
                AccountId: 1,
                LinkedAccountId: null,
                Date: new DateOnly(2020, 1, 1).AddDays(i % 500),
                Amount: Money.FromDecimal(-1m),
                CategoryId: null,
                PayeeId: null,
                Number: null,
                Memo: null,
                Cleared: ClearedStatus.Reconciled,
                IsTransfer: false,
                IsTransferSource: false,
                SplitParentId: null,
                SplitIndex: 0,
                IsScheduledInstance: false));
        }

        return new MoneyBook
        {
            Accounts = [account],
            Categories = [],
            Payees = [],
            Transactions = rows,
            Diagnostics = [],
        };
    }

    /// <summary>
    /// The check that would have caught the original bug: progress has to arrive repeatedly
    /// while the work runs, not once when it is over.
    /// </summary>
    [Fact]
    public async Task A_migration_reports_while_it_runs_rather_than_only_at_the_end()
    {
        using var harness = new BookHarness();
        var recorder = new Recorder();

        await harness.Migration.MigrateAsync(BookOf(2_000), null, recorder);

        IReadOnlyList<WorkProgress> reports = recorder.Reports;

        reports.Count.ShouldBeGreaterThan(5);
        reports.ShouldAllBe(r => !string.IsNullOrWhiteSpace(r.Stage));

        // At least one report is a real count somebody could draw a bar from.
        reports.ShouldContain(r => r.IsDeterminate && r.Done > 0);
    }

    [Fact]
    public async Task Counted_progress_never_goes_backwards_and_finishes_complete()
    {
        using var harness = new BookHarness();
        var recorder = new Recorder();

        await harness.Migration.MigrateAsync(BookOf(1_500), null, recorder);

        List<WorkProgress> counted =
        [
            .. recorder.Reports.Where(r => r.IsDeterminate && r.Stage == "Writing transactions"),
        ];

        counted.ShouldNotBeEmpty();

        for (int i = 1; i < counted.Count; i++)
        {
            counted[i].Done.ShouldBeGreaterThanOrEqualTo(counted[i - 1].Done);
        }

        counted[^1].Done.ShouldBe(counted[^1].Total);
        counted[^1].Fraction.ShouldBe(1.0, 0.001);
    }

    /// <summary>
    /// The one that matters for safety: stopping half way must leave the book as it was, not
    /// half migrated. The write runs inside a transaction, so this is really a check that the
    /// cancellation happens before the commit.
    /// </summary>
    [Fact]
    public async Task Stopping_a_migration_leaves_the_book_completely_untouched()
    {
        using var harness = new BookHarness();
        using var cancellation = new CancellationTokenSource();

        var trigger = new Progress<WorkProgress>(report =>
        {
            if (report.IsDeterminate && report.Done > 0)
            {
                cancellation.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Migration.MigrateAsync(BookOf(3_000), null, trigger, cancellation.Token));

        await using MyFinanceDbContext db = harness.CreateContext();

        (await db.Accounts.CountAsync()).ShouldBe(0);
        (await db.Transactions.CountAsync()).ShouldBe(0);
        (await db.TransactionSplits.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_migration_that_is_never_stopped_still_completes()
    {
        using var harness = new BookHarness();
        using var cancellation = new CancellationTokenSource();

        MigrationSummary summary = await harness.Migration
            .MigrateAsync(BookOf(500), null, null, cancellation.Token);

        summary.TransactionsCreated.ShouldBe(500);
    }

    /// <summary>Cancelling a backup must not disturb the one already on disk.</summary>
    [Fact]
    public async Task Stopping_a_backup_leaves_no_half_written_file()
    {
        using var temp = new TempBook();
        using Book book = temp.Create();

        await using (MyFinanceDbContext db = book.CreateContext())
        {
            db.Accounts.Add(new Account { Name = "Checking", Type = AccountType.Checking });
            await db.SaveChangesAsync();
        }

        string folder = Path.Combine(Path.GetTempPath(), "myfinance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, "book.mfbak");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Create is synchronous; the copy loop is what checks the token.
        Should.Throw<OperationCanceledException>(
            () => BackupService.Create(book, destination, null, cancellation.Token));

        File.Exists(destination).ShouldBeFalse();
        Directory.GetFiles(folder, "*.partial").ShouldBeEmpty();
    }

    [Fact]
    public void Progress_describes_itself_honestly()
    {
        var counted = new WorkProgress("Writing transactions", 12_400, 19_083);

        counted.IsDeterminate.ShouldBeTrue();
        counted.Fraction.ShouldBe(12_400 / 19_083.0, 0.0001);
        counted.CountText.ShouldContain("12,400");
        counted.Text.ShouldStartWith("Writing transactions");

        // Work with no countable total says so rather than inventing a percentage.
        WorkProgress unknown = WorkProgress.Starting("Reading the file");

        unknown.IsDeterminate.ShouldBeFalse();
        unknown.Fraction.ShouldBe(0);
        unknown.CountText.ShouldBeEmpty();
        unknown.Text.ShouldBe("Reading the file");
    }
}
