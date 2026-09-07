using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

public sealed class ReconcileServiceTests
{
    [Fact]
    public async Task A_session_opens_with_everything_not_yet_reconciled()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        int settled = await book.AddTransactionAsync(account, -100m, cleared: ClearedStatus.Reconciled);
        await book.AddTransactionAsync(account, -50m, cleared: ClearedStatus.Cleared);
        await book.AddTransactionAsync(account, -25m);

        ReconcileSession session = await book.Reconcile.BeginAsync(account);

        session.StartingBalance.ShouldBe(Money.FromDecimal(900m));
        session.Outstanding.Count.ShouldBe(2);
        session.Outstanding.ShouldNotContain(t => t.Id == settled);
        session.InitiallyCleared.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_tally_is_zero_when_the_ticked_items_match_the_statement()
    {
        var tally = new ReconcileTally(
            StartingBalance: Money.FromDecimal(900m),
            ClearedTotal: Money.FromDecimal(-75m),
            StatementBalance: Money.FromDecimal(825m));

        tally.ClearedBalance.ShouldBe(Money.FromDecimal(825m));
        tally.Difference.ShouldBe(Money.Zero);
        tally.IsBalanced.ShouldBeTrue();
    }

    [Fact]
    public async Task The_tally_reports_what_is_still_missing()
    {
        var tally = new ReconcileTally(
            StartingBalance: Money.FromDecimal(900m),
            ClearedTotal: Money.FromDecimal(-50m),
            StatementBalance: Money.FromDecimal(825m));

        tally.Difference.ShouldBe(Money.FromDecimal(-25m));
        tally.IsBalanced.ShouldBeFalse();
    }

    [Fact]
    public async Task Finishing_locks_the_ticked_items_down()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        int first = await book.AddTransactionAsync(account, -100m, new DateOnly(2026, 6, 2));
        int second = await book.AddTransactionAsync(account, -50m, new DateOnly(2026, 6, 5));
        int notOnStatement = await book.AddTransactionAsync(account, -25m, new DateOnly(2026, 6, 29));

        await book.Reconcile.CompleteAsync(
            account,
            new DateOnly(2026, 6, 30),
            Money.FromDecimal(850m),
            [first, second]);

        (await book.ReadTransactionAsync(first))!.ClearedStatus.ShouldBe(ClearedStatus.Reconciled);
        (await book.ReadTransactionAsync(second))!.ClearedStatus.ShouldBe(ClearedStatus.Reconciled);
        (await book.ReadTransactionAsync(notOnStatement))!.ClearedStatus.ShouldBe(ClearedStatus.Uncleared);

        Account? reconciled = await book.Accounts.FindAsync(account);
        reconciled!.LastReconciledOn.ShouldBe(new DateOnly(2026, 6, 30));
        reconciled.LastReconciledBalance.ShouldBe(Money.FromDecimal(850m));
    }

    [Fact]
    public async Task An_unbalanced_reconciliation_is_refused()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int id = await book.AddTransactionAsync(account, -100m);

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Reconcile.CompleteAsync(
                account,
                new DateOnly(2026, 6, 30),
                Money.FromDecimal(800m),
                [id]));

        // Recording it anyway would make the next reconciliation start from a figure the
        // bank never agreed to.
        thrown.Errors.ShouldContain(e => e.Code == ReconcileService.NotBalanced);
        (await book.ReadTransactionAsync(id))!.ClearedStatus.ShouldBe(ClearedStatus.Uncleared);
    }

    [Fact]
    public async Task A_second_reconciliation_starts_where_the_first_finished()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        int june = await book.AddTransactionAsync(account, -100m, new DateOnly(2026, 6, 2));
        await book.Reconcile.CompleteAsync(
            account, new DateOnly(2026, 6, 30), Money.FromDecimal(900m), [june]);

        int july = await book.AddTransactionAsync(account, -40m, new DateOnly(2026, 7, 3));

        ReconcileSession session = await book.Reconcile.BeginAsync(account);

        session.StartingBalance.ShouldBe(Money.FromDecimal(900m));
        session.Outstanding.Select(t => t.Id).ShouldBe([july]);

        await book.Reconcile.CompleteAsync(
            account, new DateOnly(2026, 7, 31), Money.FromDecimal(860m), [july]);

        (await book.ReadTransactionAsync(june))!.ClearedStatus.ShouldBe(ClearedStatus.Reconciled);
        (await book.ReadTransactionAsync(july))!.ClearedStatus.ShouldBe(ClearedStatus.Reconciled);
    }

    [Fact]
    public async Task Void_items_never_appear_in_a_reconciliation()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        int voided = await book.AddTransactionAsync(account, -100m);
        await book.Register.SetVoidAsync(voided, isVoid: true);

        ReconcileSession session = await book.Reconcile.BeginAsync(account);

        session.Outstanding.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reconciling_an_account_that_is_gone_is_reported_cleanly()
    {
        using var book = new BookHarness();

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Reconcile.BeginAsync(7_777));

        thrown.Errors.ShouldContain(e => e.Code == RegisterService.AccountNotFound);
    }
}
