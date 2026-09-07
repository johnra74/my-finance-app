using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;
using MyFinance.Core.Validation;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

public sealed class RegisterServiceTests
{
    [Fact]
    public async Task A_transaction_can_be_entered_and_shows_in_the_register()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 500m);

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            Date = new DateOnly(2026, 2, 3),
            Number = "1236",
            PayeeName = "Blue Bottle Coffee",
            Memo = "Beans",
            Amount = Money.FromDecimal(-18.40m),
        });

        RegisterView view = await book.Register.GetRegisterAsync(account);

        view.Lines.Count.ShouldBe(1);
        RegisterLine line = view.Lines[0];
        line.Transaction.Id.ShouldBe(id);
        line.Transaction.Number.ShouldBe("1236");
        line.Transaction.Payee!.Name.ShouldBe("Blue Bottle Coffee");
        line.Payment.ShouldBe(Money.FromDecimal(18.40m));
        line.Deposit.ShouldBeNull();
        line.Balance.ShouldBe(Money.FromDecimal(481.60m));
        view.CurrentBalance.ShouldBe(Money.FromDecimal(481.60m));
    }

    [Fact]
    public async Task Entering_a_transaction_always_leaves_at_least_one_split()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int id = await book.AddTransactionAsync(account, -10m);

        Transaction? saved = await book.Register.FindAsync(id);

        // Reports aggregate over splits alone; a transaction without one would vanish from
        // every one of them.
        saved.ShouldNotBeNull();
        saved.Splits.Count.ShouldBe(1);
        saved.Splits.Single().CategoryId.ShouldBeNull();
        saved.Splits.Single().Amount.ShouldBe(Money.FromDecimal(-10m));
    }

    [Fact]
    public async Task Splits_that_do_not_add_up_to_the_total_are_refused()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int groceries = await book.CategoryIdAsync("Food : Groceries");
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Register.SaveAsync(new TransactionDraft
            {
                AccountId = account,
                Amount = Money.FromDecimal(-100m),
                Splits =
                [
                    new SplitDraft { CategoryId = groceries, Amount = Money.FromDecimal(-60m) },
                    new SplitDraft { CategoryId = fuel, Amount = Money.FromDecimal(-30m) },
                ],
            }));

        thrown.Errors.ShouldContain(e => e.Code == TransactionValidator.SplitsDoNotSumToTotal);

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Transactions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_split_transaction_records_every_allocation()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int groceries = await book.CategoryIdAsync("Food : Groceries");
        int household = await book.CategoryIdAsync("Home : Household supplies");

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            PayeeName = "Costco",
            Amount = Money.FromDecimal(-142.83m),
            Splits =
            [
                new SplitDraft { CategoryId = groceries, Amount = Money.FromDecimal(-118.20m) },
                new SplitDraft { CategoryId = household, Amount = Money.FromDecimal(-24.63m), Memo = "Detergent" },
            ],
        });

        Transaction? saved = await book.Register.FindAsync(id);

        saved.ShouldNotBeNull();
        saved.IsSplit.ShouldBeTrue();
        saved.Splits.Count.ShouldBe(2);
        Money.Sum(saved.Splits.Select(s => s.Amount)).ShouldBe(Money.FromDecimal(-142.83m));
    }

    [Fact]
    public async Task Editing_a_transaction_replaces_its_splits_rather_than_accumulating_them()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int groceries = await book.CategoryIdAsync("Food : Groceries");
        int restaurants = await book.CategoryIdAsync("Food : Restaurants");

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            Amount = Money.FromDecimal(-50m),
            Splits = [new SplitDraft { CategoryId = groceries, Amount = Money.FromDecimal(-50m) }],
        });

        await book.Register.SaveAsync(new TransactionDraft
        {
            Id = id,
            AccountId = account,
            Amount = Money.FromDecimal(-50m),
            Splits = [new SplitDraft { CategoryId = restaurants, Amount = Money.FromDecimal(-50m) }],
        });

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.TransactionSplits.CountAsync()).ShouldBe(1);
        (await db.TransactionSplits.SingleAsync()).CategoryId.ShouldBe(restaurants);
    }

    [Fact]
    public async Task Editing_a_transaction_changes_the_balance()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 100m);
        int id = await book.AddTransactionAsync(account, -10m);

        await book.Register.SaveAsync(new TransactionDraft
        {
            Id = id,
            AccountId = account,
            Date = new DateOnly(2026, 1, 15),
            Amount = Money.FromDecimal(-25m),
        });

        RegisterView view = await book.Register.GetRegisterAsync(account);
        view.CurrentBalance.ShouldBe(Money.FromDecimal(75m));
        view.Lines.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_deleted_transaction_is_gone_along_with_its_splits()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 100m);
        int id = await book.AddTransactionAsync(account, -10m);

        await book.Register.DeleteAsync(id);

        RegisterView view = await book.Register.GetRegisterAsync(account);
        view.Lines.ShouldBeEmpty();
        view.CurrentBalance.ShouldBe(Money.FromDecimal(100m));

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.TransactionSplits.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_transaction_that_is_gone_is_reported_cleanly()
    {
        using var book = new BookHarness();

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Register.DeleteAsync(4_242));

        thrown.Errors.ShouldContain(e => e.Code == RegisterService.NotFound);
    }

    [Fact]
    public async Task A_transfer_writes_a_matching_row_in_the_other_account()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Date = new DateOnly(2026, 4, 1),
            Amount = Money.FromDecimal(-400m),
            TransferAccountId = savings,
            Memo = "Monthly saving",
        });

        Transaction? near = await book.Register.FindAsync(id);
        near.ShouldNotBeNull();
        near.TransferPeerId.ShouldNotBeNull();

        Transaction? far = await book.Register.FindAsync(near.TransferPeerId!.Value);
        far.ShouldNotBeNull();
        far.AccountId.ShouldBe(savings);
        far.Amount.ShouldBe(Money.FromDecimal(400m));
        far.Date.ShouldBe(near.Date);
        far.TransferPeerId.ShouldBe(id);

        (await book.Register.GetRegisterAsync(checking)).CurrentBalance
            .ShouldBe(Money.FromDecimal(600m));
        (await book.Register.GetRegisterAsync(savings)).CurrentBalance
            .ShouldBe(Money.FromDecimal(400m));
    }

    [Fact]
    public async Task Changing_a_transfer_amount_moves_both_legs()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Date = new DateOnly(2026, 4, 1),
            Amount = Money.FromDecimal(-400m),
            TransferAccountId = savings,
        });

        await book.Register.SaveAsync(new TransactionDraft
        {
            Id = id,
            AccountId = checking,
            Date = new DateOnly(2026, 4, 1),
            Amount = Money.FromDecimal(-250m),
            TransferAccountId = savings,
        });

        (await book.Register.GetRegisterAsync(checking)).CurrentBalance
            .ShouldBe(Money.FromDecimal(750m));
        (await book.Register.GetRegisterAsync(savings)).CurrentBalance
            .ShouldBe(Money.FromDecimal(250m));

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Transactions.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Retargeting_a_transfer_moves_the_far_leg_to_the_new_account()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);
        int money = await book.AddAccountAsync("Money market", AccountType.MoneyMarket);

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = savings,
        });

        await book.Register.SaveAsync(new TransactionDraft
        {
            Id = id,
            AccountId = checking,
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = money,
        });

        (await book.Register.GetRegisterAsync(savings)).CurrentBalance.ShouldBe(Money.Zero);
        (await book.Register.GetRegisterAsync(money)).CurrentBalance.ShouldBe(Money.FromDecimal(100m));

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Transactions.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Turning_a_transfer_into_an_ordinary_payment_removes_the_far_leg()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = savings,
        });

        await book.Register.SaveAsync(new TransactionDraft
        {
            Id = id,
            AccountId = checking,
            Amount = Money.FromDecimal(-100m),
            PayeeName = "Hardware store",
            TransferAccountId = null,
        });

        Transaction? near = await book.Register.FindAsync(id);
        near.ShouldNotBeNull();
        near.TransferPeerId.ShouldBeNull();

        (await book.Register.GetRegisterAsync(savings)).CurrentBalance.ShouldBe(Money.Zero);

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Transactions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_one_leg_of_a_transfer_deletes_the_other()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = savings,
        });

        await book.Register.DeleteAsync(id);

        (await book.Register.GetRegisterAsync(savings)).CurrentBalance.ShouldBe(Money.Zero);

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Transactions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_both_legs_at_once_is_not_an_error()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = savings,
        });

        Transaction? near = await book.Register.FindAsync(id);
        int peerId = near!.TransferPeerId!.Value;

        // Selecting both legs and pressing delete is a reasonable thing to do; removing the
        // first takes the second with it, and the second must not then report an error.
        int removed = await book.Register.DeleteManyAsync([id, peerId]);

        removed.ShouldBe(1);

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Transactions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_is_refused()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Register.SaveAsync(new TransactionDraft
            {
                AccountId = checking,
                Amount = Money.FromDecimal(-10m),
                TransferAccountId = checking,
            }));

        thrown.Errors.ShouldContain(e => e.Code == RegisterService.TransferTargetSame);
    }

    [Fact]
    public async Task Voiding_a_transfer_voids_both_legs()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = savings,
        });

        await book.Register.SetVoidAsync(id, isVoid: true);

        // If only one leg voided, the two accounts would disagree about whether the money
        // moved and net worth would change by the transfer amount.
        (await book.Register.GetRegisterAsync(checking)).CurrentBalance
            .ShouldBe(Money.FromDecimal(1_000m));
        (await book.Register.GetRegisterAsync(savings)).CurrentBalance.ShouldBe(Money.Zero);
    }

    [Fact]
    public async Task The_running_balance_is_stable_for_transactions_sharing_a_date()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        var day = new DateOnly(2026, 5, 6);

        await book.AddTransactionAsync(account, -100m, day);
        await book.AddTransactionAsync(account, -200m, day);
        await book.AddTransactionAsync(account, 50m, day);

        RegisterView view = await book.Register.GetRegisterAsync(account);

        view.Lines.Select(l => l.Balance).ShouldBe(
        [
            Money.FromDecimal(900m),
            Money.FromDecimal(700m),
            Money.FromDecimal(750m),
        ]);
    }

    [Fact]
    public async Task A_back_dated_entry_lands_in_the_right_place_in_the_register()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        await book.AddTransactionAsync(account, -100m, new DateOnly(2026, 5, 10));
        await book.AddTransactionAsync(account, -50m, new DateOnly(2026, 5, 1));

        RegisterView view = await book.Register.GetRegisterAsync(account);

        view.Lines[0].Transaction.Date.ShouldBe(new DateOnly(2026, 5, 1));
        view.Lines[0].Balance.ShouldBe(Money.FromDecimal(950m));
        view.Lines[1].Balance.ShouldBe(Money.FromDecimal(850m));
    }

    [Fact]
    public async Task Filtering_the_register_keeps_the_balance_column_meaningful()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        await book.AddTransactionAsync(account, -100m, new DateOnly(2026, 1, 5));
        await book.AddTransactionAsync(account, -200m, new DateOnly(2026, 2, 5));
        await book.AddTransactionAsync(account, -300m, new DateOnly(2026, 3, 5));

        RegisterView view = await book.Register.GetRegisterAsync(
            account,
            new RegisterFilter { From = new DateOnly(2026, 2, 1) });

        view.Lines.Count.ShouldBe(2);
        view.IsFiltered.ShouldBeTrue();

        // The balance still runs from the start of the account, not from the filter edge.
        view.Lines[0].Balance.ShouldBe(Money.FromDecimal(700m));
        view.Lines[1].Balance.ShouldBe(Money.FromDecimal(400m));
        view.CurrentBalance.ShouldBe(Money.FromDecimal(400m));
    }

    [Fact]
    public async Task The_register_can_be_searched_by_payee()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.AddTransactionAsync(account, -10m, payee: "Blue Bottle Coffee");
        await book.AddTransactionAsync(account, -20m, payee: "Shell");

        RegisterView view = await book.Register.GetRegisterAsync(
            account,
            new RegisterFilter { Search = "bottle" });

        view.Lines.Count.ShouldBe(1);
        view.Lines[0].Transaction.Payee!.Name.ShouldBe("Blue Bottle Coffee");
    }

    [Fact]
    public async Task Payees_are_reused_rather_than_duplicated()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.AddTransactionAsync(account, -10m, payee: "Blue Bottle Coffee");
        await book.AddTransactionAsync(account, -12m, payee: "BLUE BOTTLE COFFEE.");

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Payees.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_payee_remembers_the_category_and_amount_last_used()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        await book.AddTransactionAsync(account, -18.40m, payee: "Blue Bottle Coffee", categoryId: coffee);

        PayeeListItem? payee = await book.Payees.FindByNameAsync("blue bottle coffee");

        payee.ShouldNotBeNull();
        payee.LastCategoryId.ShouldBe(coffee);
        payee.LastAmount.ShouldBe(Money.FromDecimal(-18.40m));
        payee.LastCategoryName.ShouldBe("Food : Coffee");
    }

    [Fact]
    public async Task A_split_transaction_teaches_the_payee_no_single_category()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int groceries = await book.CategoryIdAsync("Food : Groceries");
        int household = await book.CategoryIdAsync("Home : Household supplies");

        await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            PayeeName = "Costco",
            Amount = Money.FromDecimal(-100m),
            Splits =
            [
                new SplitDraft { CategoryId = groceries, Amount = Money.FromDecimal(-70m) },
                new SplitDraft { CategoryId = household, Amount = Money.FromDecimal(-30m) },
            ],
        });

        PayeeListItem? payee = await book.Payees.FindByNameAsync("Costco");

        // Guessing one of the two would pre-fill the wrong answer next time.
        payee.ShouldNotBeNull();
        payee.LastCategoryId.ShouldBeNull();
        payee.LastAmount.ShouldBe(Money.FromDecimal(-100m));
    }

    [Fact]
    public async Task A_transaction_with_no_payee_is_allowed()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        int id = await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            Amount = Money.FromDecimal(-60m),
            Memo = "ATM",
        });

        Transaction? saved = await book.Register.FindAsync(id);
        saved.ShouldNotBeNull();
        saved.PayeeId.ShouldBeNull();
    }

    [Fact]
    public async Task Clearing_a_transaction_moves_the_bank_balance_only()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 100m);
        int id = await book.AddTransactionAsync(account, -30m);

        RegisterView before = await book.Register.GetRegisterAsync(account);
        before.ClearedBalance.ShouldBe(Money.FromDecimal(100m));

        await book.Register.SetClearedStatusAsync(id, ClearedStatus.Cleared);

        RegisterView after = await book.Register.GetRegisterAsync(account);
        after.ClearedBalance.ShouldBe(Money.FromDecimal(70m));
        after.CurrentBalance.ShouldBe(Money.FromDecimal(70m));
        after.ReconciledBalance.ShouldBe(Money.FromDecimal(100m));
    }

    [Fact]
    public async Task Assigning_a_category_clears_it_from_the_worklist()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        int id = await book.AddTransactionAsync(account, -4.50m, payee: "Blue Bottle Coffee");

        (await book.Register.GetUncategorizedAsync()).Count.ShouldBe(1);

        await book.Register.SetCategoryAsync(id, coffee);

        (await book.Register.GetUncategorizedAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Transfers_are_not_listed_as_needing_a_category()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 500m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        await book.Register.SaveAsync(new TransactionDraft
        {
            AccountId = checking,
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = savings,
        });

        // Moving money between your own accounts is not spending, so it never needs a
        // category and must not sit in the triage list forever.
        (await book.Register.GetUncategorizedAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Writing_to_an_account_that_is_gone_is_reported_cleanly()
    {
        using var book = new BookHarness();

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Register.SaveAsync(new TransactionDraft
            {
                AccountId = 8_888,
                Amount = Money.FromDecimal(-1m),
            }));

        thrown.Errors.ShouldContain(e => e.Code == RegisterService.AccountNotFound);
    }

    [Fact]
    public async Task An_imported_balance_only_account_cannot_be_written_to()
    {
        using var book = new BookHarness();
        int mortgage = await book.AddAccountAsync("Woodgrove Mortgage", AccountType.UnsupportedImported);

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Register.SaveAsync(new TransactionDraft
            {
                AccountId = mortgage,
                Amount = Money.FromDecimal(-1m),
            }));

        thrown.Errors.ShouldContain(e => e.Code == RegisterService.AccountReadOnly);
    }

    [Fact]
    public async Task A_credit_card_balance_is_negative_while_money_is_owed()
    {
        using var book = new BookHarness();
        int card = await book.AddAccountAsync("Rewards Card", AccountType.CreditCard);

        await book.AddTransactionAsync(card, -410.25m, payee: "Various");

        RegisterView view = await book.Register.GetRegisterAsync(card);

        // A card you owe money on reads as a negative balance, shown in parentheses — the
        // same way Microsoft Money shows it, which is what makes the two comparable.
        view.CurrentBalance.ShouldBe(Money.FromDecimal(-410.25m));
        view.CurrentBalance.ToAccountingString(CultureInfo.GetCultureInfo("en-US"))
            .ShouldBe("($410.25)");
    }
}
