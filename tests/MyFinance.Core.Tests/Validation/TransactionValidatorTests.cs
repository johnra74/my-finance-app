using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Core.Validation;
using CoreTransaction = MyFinance.Core.Entities.Transaction;

namespace MyFinance.Core.Tests.Validation;

public class TransactionValidatorTests
{
    [Fact]
    public void A_well_formed_transaction_validates()
    {
        TransactionValidator.Validate(TestBook.Transaction(-100m)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_transaction_with_no_splits_is_rejected()
    {
        CoreTransaction transaction = TestBook.Transaction(-100m);
        transaction.Splits.Clear();

        ValidationResult result = TransactionValidator.Validate(transaction);

        result.IsValid.ShouldBeFalse();
        result.Errors.Select(e => e.Code).ShouldContain(TransactionValidator.SplitsMissing);
    }

    [Fact]
    public void Splits_that_do_not_sum_to_the_total_are_rejected()
    {
        CoreTransaction transaction = TestBook.Transaction(-100m);
        transaction.Splits.Clear();
        transaction.Splits.Add(new TransactionSplit { Amount = Money.FromDecimal(-60m) });
        transaction.Splits.Add(new TransactionSplit { Amount = Money.FromDecimal(-30m) });

        ValidationResult result = TransactionValidator.Validate(transaction);

        result.IsValid.ShouldBeFalse();
        result.Errors.Select(e => e.Code).ShouldContain(TransactionValidator.SplitsDoNotSumToTotal);
    }

    [Fact]
    public void Splits_summing_exactly_to_the_total_are_accepted()
    {
        CoreTransaction transaction = TestBook.Transaction(-100m);
        transaction.Splits.Clear();
        transaction.Splits.Add(new TransactionSplit { Amount = Money.FromDecimal(-60m) });
        transaction.Splits.Add(new TransactionSplit { Amount = Money.FromDecimal(-40m) });

        TransactionValidator.Validate(transaction).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void An_allocated_split_always_satisfies_the_sum_invariant()
    {
        // A three-way split of $10.00 cannot be done with equal shares without losing a cent;
        // Money.Allocate is what keeps the invariant satisfiable.
        CoreTransaction transaction = TestBook.Transaction(-10m);
        transaction.Splits.Clear();
        foreach (Money share in Money.FromDecimal(-10m).Allocate(3))
        {
            transaction.Splits.Add(new TransactionSplit { Amount = share });
        }

        TransactionValidator.Validate(transaction).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_date_before_1900_is_rejected_as_a_misread_import()
    {
        CoreTransaction transaction = TestBook.Transaction(-100m, "0001-01-01");

        TransactionValidator.Validate(transaction).Errors
            .Select(e => e.Code)
            .ShouldContain(TransactionValidator.DateOutOfRange);
    }

    [Fact]
    public void All_failures_are_reported_together_not_just_the_first()
    {
        CoreTransaction transaction = TestBook.Transaction(-100m, "0001-01-01");
        transaction.Splits.Clear();

        TransactionValidator.Validate(transaction).Errors.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    // -- Transfers ----------------------------------------------------------------------

    [Fact]
    public void A_matched_transfer_pair_validates()
    {
        CoreTransaction near = TestBook.Transaction(-500m, "2026-01-15", accountId: 1);
        CoreTransaction far = TestBook.Transaction(500m, "2026-01-15", accountId: 2);

        TransactionValidator.ValidateTransferPair(near, far).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Transfer_legs_that_do_not_cancel_out_are_rejected()
    {
        CoreTransaction near = TestBook.Transaction(-500m, "2026-01-15", accountId: 1);
        CoreTransaction far = TestBook.Transaction(499m, "2026-01-15", accountId: 2);

        TransactionValidator.ValidateTransferPair(near, far).Errors
            .Select(e => e.Code)
            .ShouldContain(TransactionValidator.TransferPeerMismatch);
    }

    [Fact]
    public void A_transfer_within_a_single_account_is_rejected()
    {
        CoreTransaction near = TestBook.Transaction(-500m, "2026-01-15", accountId: 7);
        CoreTransaction far = TestBook.Transaction(500m, "2026-01-15", accountId: 7);

        TransactionValidator.ValidateTransferPair(near, far).Errors
            .Select(e => e.Code)
            .ShouldContain(TransactionValidator.TransferSameAccount);
    }

    [Fact]
    public void Transfer_legs_on_different_dates_are_rejected()
    {
        CoreTransaction near = TestBook.Transaction(-500m, "2026-01-15", accountId: 1);
        CoreTransaction far = TestBook.Transaction(500m, "2026-01-16", accountId: 2);

        TransactionValidator.ValidateTransferPair(near, far).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_transfer_pointing_at_itself_is_rejected()
    {
        CoreTransaction transaction = TestBook.Transaction(-500m, id: 42);
        transaction.TransferPeerId = 42;

        TransactionValidator.ValidateTransferLinkage(transaction).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Transfer_pairs_always_net_to_zero_across_the_books()
    {
        CoreTransaction near = TestBook.Transaction(-1234.56m, "2026-01-15", accountId: 1);
        CoreTransaction far = TestBook.Transaction(1234.56m, "2026-01-15", accountId: 2);

        (near.Amount + far.Amount).ShouldBe(Money.Zero);
    }
}
