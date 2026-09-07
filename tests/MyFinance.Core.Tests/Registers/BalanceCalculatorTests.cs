using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;
using CoreTransaction = MyFinance.Core.Entities.Transaction;

namespace MyFinance.Core.Tests.Registers;

public class BalanceCalculatorTests
{
    [Fact]
    public void Register_is_empty_when_there_are_no_transactions()
    {
        BalanceCalculator.BuildRegister(Money.Zero, []).ShouldBeEmpty();
    }

    [Fact]
    public void Running_balance_is_the_prefix_sum_of_amounts()
    {
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(100m, "2026-01-01"),
            TestBook.Transaction(-30m, "2026-01-02"),
            TestBook.Transaction(-20m, "2026-01-03"),
        ];

        IReadOnlyList<RegisterLine> register =
            BalanceCalculator.BuildRegister(Money.FromDecimal(50m), transactions);

        register.Select(l => l.Balance.ToDecimal()).ShouldBe([150m, 120m, 100m]);
        register.Select(l => l.Index).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void Running_balances_survive_a_whole_month_of_mixed_amounts()
    {
        // The shape a real register has, on invented figures: a deposit lands on an opening
        // balance and payments walk it down, with cents that have to carry correctly at every
        // step. The short case above proves the prefix sum; this one proves it does not drift.
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(3450.00m, "2026-03-31"),
            TestBook.Transaction(-1275.40m, "2026-04-01"),
            TestBook.Transaction(-100.00m, "2026-04-06"),
            TestBook.Transaction(-44.99m, "2026-04-07"),
            TestBook.Transaction(-2150.75m, "2026-04-08"),
            TestBook.Transaction(-195.53m, "2026-04-09"),
        ];

        IReadOnlyList<RegisterLine> register =
            BalanceCalculator.BuildRegister(Money.FromDecimal(1200.00m), transactions);

        register.Select(l => l.Balance.ToDecimal())
            .ShouldBe([4650.00m, 3374.60m, 3274.60m, 3229.61m, 1078.86m, 883.33m]);
    }

    [Fact]
    public void Transactions_sharing_a_date_are_ordered_by_sequence_then_id()
    {
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(-10m, "2026-01-01", sequenceInDay: 2, id: 300),
            TestBook.Transaction(-20m, "2026-01-01", sequenceInDay: 1, id: 200),
            TestBook.Transaction(-30m, "2026-01-01", sequenceInDay: 1, id: 100),
        ];

        IReadOnlyList<RegisterLine> register = BalanceCalculator.BuildRegister(Money.Zero, transactions);

        register.Select(l => l.Transaction.Id).ShouldBe([100, 200, 300]);
    }

    [Fact]
    public void Register_order_is_stable_regardless_of_input_order()
    {
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(-10m, "2026-03-05", sequenceInDay: 0, id: 3),
            TestBook.Transaction(-20m, "2026-01-05", sequenceInDay: 0, id: 1),
            TestBook.Transaction(-30m, "2026-02-05", sequenceInDay: 0, id: 2),
        ];

        int[] forward = BalanceCalculator.BuildRegister(Money.Zero, transactions)
            .Select(l => l.Transaction.Id).ToArray();
        int[] reversed = BalanceCalculator.BuildRegister(Money.Zero, transactions.Reverse())
            .Select(l => l.Transaction.Id).ToArray();

        forward.ShouldBe(reversed);
        forward.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Voided_transactions_stay_visible_but_do_not_move_the_balance()
    {
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(100m, "2026-01-01"),
            TestBook.Transaction(-500m, "2026-01-02", isVoid: true),
            TestBook.Transaction(-25m, "2026-01-03"),
        ];

        IReadOnlyList<RegisterLine> register = BalanceCalculator.BuildRegister(Money.Zero, transactions);

        register.Count.ShouldBe(3);
        register.Select(l => l.Balance.ToDecimal()).ShouldBe([100m, 100m, 75m]);
    }

    [Fact]
    public void Payment_and_deposit_columns_split_on_the_sign()
    {
        IReadOnlyList<RegisterLine> register = BalanceCalculator.BuildRegister(
            Money.Zero,
            [TestBook.Transaction(-44.99m, "2026-01-01"), TestBook.Transaction(6975.71m, "2026-01-02")]);

        register[0].Payment!.Value.ToDecimal().ShouldBe(44.99m);
        register[0].Deposit.ShouldBeNull();
        register[1].Deposit!.Value.ToDecimal().ShouldBe(6975.71m);
        register[1].Payment.ShouldBeNull();
    }

    // -- The several balances an account has -------------------------------------------

    [Fact]
    public void Current_cleared_and_reconciled_balances_each_count_a_different_subset()
    {
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(1000m, "2026-01-01", ClearedStatus.Reconciled),
            TestBook.Transaction(-200m, "2026-01-02", ClearedStatus.Cleared),
            TestBook.Transaction(-50m, "2026-01-03", ClearedStatus.Uncleared),
        ];

        Money opening = Money.FromDecimal(100m);

        BalanceCalculator.ReconciledBalance(opening, transactions).ToDecimal().ShouldBe(1100m);
        BalanceCalculator.ClearedBalance(opening, transactions).ToDecimal().ShouldBe(900m);
        BalanceCalculator.CurrentBalance(opening, transactions).ToDecimal().ShouldBe(850m);
    }

    [Fact]
    public void Balances_exclude_voided_transactions_at_every_cleared_level()
    {
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(-999m, "2026-01-01", ClearedStatus.Reconciled, isVoid: true),
        ];

        BalanceCalculator.ReconciledBalance(Money.Zero, transactions).ShouldBe(Money.Zero);
        BalanceCalculator.ClearedBalance(Money.Zero, transactions).ShouldBe(Money.Zero);
        BalanceCalculator.CurrentBalance(Money.Zero, transactions).ShouldBe(Money.Zero);
    }

    [Fact]
    public void Balance_as_of_a_date_ignores_later_transactions()
    {
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(100m, "2026-01-01"),
            TestBook.Transaction(200m, "2026-06-01"),
            TestBook.Transaction(400m, "2026-12-01"),
        ];

        BalanceCalculator
            .BalanceAsOf(Money.Zero, transactions, new DateOnly(2026, 6, 30))
            .ToDecimal()
            .ShouldBe(300m);
    }

    [Fact]
    public void Balance_as_of_includes_transactions_dated_that_exact_day()
    {
        CoreTransaction[] transactions = [TestBook.Transaction(100m, "2026-06-30")];

        BalanceCalculator
            .BalanceAsOf(Money.Zero, transactions, new DateOnly(2026, 6, 30))
            .ToDecimal()
            .ShouldBe(100m);
    }

    [Fact]
    public void Final_register_balance_equals_the_current_balance()
    {
        CoreTransaction[] transactions =
        [
            TestBook.Transaction(-2385.84m, "2026-05-01"),
            TestBook.Transaction(-44.99m, "2026-05-07"),
            TestBook.Transaction(-195.48m, "2026-05-09"),
        ];
        Money opening = Money.FromDecimal(-2283.63m);

        IReadOnlyList<RegisterLine> register = BalanceCalculator.BuildRegister(opening, transactions);

        register[^1].Balance.ShouldBe(BalanceCalculator.CurrentBalance(opening, transactions));
        register[^1].Balance.ToDecimal().ShouldBe(-4909.94m);
    }
}
