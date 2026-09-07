using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Core.Validation;

namespace MyFinance.Core.Tests.Validation;

/// <summary>
/// The transfer invariant, and the one place multi-currency deliberately weakens it.
/// </summary>
/// <remarks>
/// <see cref="TransactionValidatorTests"/> still passes unmodified, including
/// <c>Transfer_pairs_always_net_to_zero_across_the_books</c> — the same-currency rule is
/// exactly as strict as it has always been. These cover the case it cannot speak to.
/// </remarks>
public class CrossCurrencyTransferTests
{
    private static Transaction Leg(int id, int accountId, decimal amount, Currency currency, int peerId) => new()
    {
        Id = id,
        AccountId = accountId,
        Date = new DateOnly(2026, 3, 1),
        Amount = Money.FromDecimal(amount, currency),
        TransferPeerId = peerId,
        Splits = [new TransactionSplit { Amount = Money.FromDecimal(amount, currency) }],
    };

    [Fact]
    public void A_same_currency_pair_must_still_cancel_exactly()
    {
        // Unchanged, and it must stay that way: this is the assertion that keeps moving money
        // between your own accounts from creating or destroying value.
        Transaction near = Leg(1, 10, -200m, Currency.Usd, 2);
        Transaction far = Leg(2, 20, 199m, Currency.Usd, 1);

        TransactionValidator.ValidateTransferPair(near, far).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_cross_currency_transfer_pair_is_valid_without_cancelling()
    {
        // $100 leaves, £78 arrives — what the two statements actually say. There is no rate
        // here to check that against, so the arithmetic is not asserted.
        Transaction near = Leg(1, 10, -100m, Currency.Usd, 2);
        Transaction far = Leg(2, 20, 78m, Currency.Gbp, 1);

        TransactionValidator.ValidateTransferPair(near, far).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_cross_currency_pair_must_still_name_each_other_and_share_a_date()
    {
        Transaction near = Leg(1, 10, -100m, Currency.Usd, 2);
        Transaction far = Leg(2, 20, 78m, Currency.Gbp, 1);
        far.Date = new DateOnly(2026, 3, 2);

        TransactionValidator.ValidateTransferPair(near, far).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_cross_currency_pair_still_cannot_sit_in_one_account()
    {
        Transaction near = Leg(1, 10, -100m, Currency.Usd, 2);
        Transaction far = Leg(2, 10, 78m, Currency.Gbp, 1);

        TransactionValidator.ValidateTransferPair(near, far).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Both_legs_pointing_the_same_way_is_refused_whatever_the_currencies()
    {
        // The one thing still checkable without a rate: money left one account, so it must
        // have arrived in the other. Two legs the same way creates value at any rate.
        Transaction near = Leg(1, 10, -100m, Currency.Usd, 2);
        Transaction far = Leg(2, 20, -78m, Currency.Gbp, 1);

        TransactionValidator.ValidateTransferPair(near, far).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validating_a_cross_currency_pair_never_throws_on_the_comparison()
    {
        // The validator compares the two amounts. Doing that naively across currencies would
        // throw from Money itself rather than returning a verdict.
        Transaction near = Leg(1, 10, -100m, Currency.Usd, 2);
        Transaction far = Leg(2, 20, 78m, Currency.Gbp, 1);

        Should.NotThrow(() => TransactionValidator.ValidateTransferPair(near, far));
    }
}
