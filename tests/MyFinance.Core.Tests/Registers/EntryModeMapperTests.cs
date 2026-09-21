namespace MyFinance.Core.Tests.Registers;

using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;
using Shouldly;
using Xunit;

public class EntryModeMapperTests
{
    [Fact]
    public void Payment_direction_produces_negative_amount()
    {
        var entered = Money.FromMinorUnits(5000); // $50.00

        var signed = EntryModeMapper.MapAmount(EntryMode.Payment, entered);

        signed.MinorUnits.ShouldBe(-5000);
    }

    [Fact]
    public void Deposit_direction_produces_positive_amount()
    {
        var entered = Money.FromMinorUnits(5000);

        var signed = EntryModeMapper.MapAmount(EntryMode.Deposit, entered);

        signed.MinorUnits.ShouldBe(5000);
    }

    [Fact]
    public void Transfer_mode_produces_negative_amount()
    {
        var entered = Money.FromMinorUnits(5000);

        var signed = EntryModeMapper.MapAmount(EntryMode.Transfer, entered);

        signed.MinorUnits.ShouldBe(-5000);
    }

    [Fact]
    public void Amount_sign_applies_regardless_of_magnitude()
    {
        var entered = Money.FromMinorUnits(1);

        var signed = EntryModeMapper.MapAmount(EntryMode.Payment, entered);

        signed.MinorUnits.ShouldBe(-1);
    }

    [Fact]
    public void Zero_amount_stays_zero()
    {
        var entered = Money.FromMinorUnits(0);

        var paymentSigned = EntryModeMapper.MapAmount(EntryMode.Payment, entered);
        var depositSigned = EntryModeMapper.MapAmount(EntryMode.Deposit, entered);

        paymentSigned.MinorUnits.ShouldBe(0);
        depositSigned.MinorUnits.ShouldBe(0);
    }

    [Fact]
    public void Category_allowed_in_payment_mode()
    {
        EntryModeMapper.IsCategoryAllowed(EntryMode.Payment).ShouldBeTrue();
    }

    [Fact]
    public void Category_allowed_in_deposit_mode()
    {
        EntryModeMapper.IsCategoryAllowed(EntryMode.Deposit).ShouldBeTrue();
    }

    [Fact]
    public void Category_not_allowed_in_transfer_mode()
    {
        EntryModeMapper.IsCategoryAllowed(EntryMode.Transfer).ShouldBeFalse();
    }

    [Fact]
    public void Transfer_target_allowed_in_transfer_mode()
    {
        EntryModeMapper.IsTransferTargetAllowed(EntryMode.Transfer).ShouldBeTrue();
    }

    [Fact]
    public void Transfer_target_not_allowed_in_payment_mode()
    {
        EntryModeMapper.IsTransferTargetAllowed(EntryMode.Payment).ShouldBeFalse();
    }

    [Fact]
    public void Transfer_target_not_allowed_in_deposit_mode()
    {
        EntryModeMapper.IsTransferTargetAllowed(EntryMode.Deposit).ShouldBeFalse();
    }

    [Fact]
    public void Normalize_category_clears_in_transfer_mode()
    {
        var normalized = EntryModeMapper.NormalizeCategory(EntryMode.Transfer, 5);

        normalized.ShouldBeNull();
    }

    [Fact]
    public void Normalize_category_preserves_in_payment_mode()
    {
        var normalized = EntryModeMapper.NormalizeCategory(EntryMode.Payment, 5);

        normalized.ShouldBe(5);
    }

    [Fact]
    public void Normalize_category_preserves_in_deposit_mode()
    {
        var normalized = EntryModeMapper.NormalizeCategory(EntryMode.Deposit, 5);

        normalized.ShouldBe(5);
    }

    [Fact]
    public void Normalize_category_null_stays_null()
    {
        var normalized = EntryModeMapper.NormalizeCategory(EntryMode.Payment, null);

        normalized.ShouldBeNull();
    }

    [Fact]
    public void Validate_transfer_target_requires_non_null_in_transfer_mode()
    {
        EntryModeMapper.ValidateTransferTarget(EntryMode.Transfer, null).ShouldBeFalse();
        EntryModeMapper.ValidateTransferTarget(EntryMode.Transfer, 5).ShouldBeTrue();
    }

    [Fact]
    public void Validate_transfer_target_requires_null_in_payment_mode()
    {
        EntryModeMapper.ValidateTransferTarget(EntryMode.Payment, null).ShouldBeTrue();
        EntryModeMapper.ValidateTransferTarget(EntryMode.Payment, 5).ShouldBeFalse();
    }

    [Fact]
    public void Validate_transfer_target_requires_null_in_deposit_mode()
    {
        EntryModeMapper.ValidateTransferTarget(EntryMode.Deposit, null).ShouldBeTrue();
        EntryModeMapper.ValidateTransferTarget(EntryMode.Deposit, 5).ShouldBeFalse();
    }

    [Fact]
    public void Validate_transfer_not_same_account_allows_different_accounts()
    {
        EntryModeMapper.ValidateTransferNotSameAccount(1, 2).ShouldBeTrue();
    }

    [Fact]
    public void Validate_transfer_not_same_account_rejects_same_account()
    {
        EntryModeMapper.ValidateTransferNotSameAccount(5, 5).ShouldBeFalse();
    }

    [Fact]
    public void Validate_transfer_not_same_account_allows_null_target()
    {
        EntryModeMapper.ValidateTransferNotSameAccount(5, null).ShouldBeTrue();
    }

    [Fact]
    public void Mode_transition_payment_to_deposit_changes_sign()
    {
        var amount = Money.FromMinorUnits(5000);
        var paymentSigned = EntryModeMapper.MapAmount(EntryMode.Payment, amount);
        var depositSigned = EntryModeMapper.MapAmount(EntryMode.Deposit, amount);

        paymentSigned.MinorUnits.ShouldBe(-5000);
        depositSigned.MinorUnits.ShouldBe(5000);
    }

    [Fact]
    public void Mode_transition_payment_to_transfer_same_sign()
    {
        var amount = Money.FromMinorUnits(5000);
        var paymentSigned = EntryModeMapper.MapAmount(EntryMode.Payment, amount);
        var transferSigned = EntryModeMapper.MapAmount(EntryMode.Transfer, amount);

        paymentSigned.MinorUnits.ShouldBe(transferSigned.MinorUnits);
    }

    [Fact]
    public void MapAmount_rejects_negative_input()
    {
        var negativeAmount = Money.FromMinorUnits(-5000);

        Should.Throw<ArgumentOutOfRangeException>(
            () => EntryModeMapper.MapAmount(EntryMode.Payment, negativeAmount)
        );
    }
}
