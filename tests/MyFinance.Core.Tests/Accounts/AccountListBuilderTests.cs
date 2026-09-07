using MyFinance.Core.Accounts;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests.Accounts;

public sealed class AccountListBuilderTests
{
    [Fact]
    public void Groups_appear_in_the_order_the_banking_screen_shows_them()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Summary("Rewards Card", AccountType.CreditCard, -410.25m),
            Summary("Everyday Checking", AccountType.Checking, -1_240.50m),
        ]);

        list.Groups.Select(g => g.Group).ShouldBe([AccountGroup.Bank, AccountGroup.Credit]);
        list.Groups.Select(g => g.Header).ShouldBe(["Bank accounts", "Credit cards"]);
    }

    [Fact]
    public void Empty_groups_are_left_out_rather_than_shown_with_nothing_under_them()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Summary("Everyday Checking", AccountType.Checking, 100m),
        ]);

        list.Groups.Count.ShouldBe(1);
        list.Groups[0].Group.ShouldBe(AccountGroup.Bank);
    }

    [Fact]
    public void Subtotals_add_up_within_a_group_and_the_total_across_them()
    {
        // Invented figures, chosen so that a group carries both signs, the cents have to
        // carry, and the grand total is not the sum of either group on its own. The
        // arithmetic is what is under test; a real account list is nobody's business but its
        // owner's, and does not belong in a repository.
        AccountListSummary list = AccountListBuilder.Build(
        [
            Summary("Rainy Day Savings", AccountType.Savings, 14_395.78m),
            Summary("Holiday Fund", AccountType.Savings, 2_500.16m),
            Summary("Everyday Checking", AccountType.Checking, -1_240.50m),
            Summary("Emergency Fund", AccountType.Savings, 8_600.88m),
            Summary("Rewards Card", AccountType.CreditCard, -410.25m),
            Summary("Store Card", AccountType.CreditCard, -2_564.36m),
        ]);

        list.Groups[0].Subtotal.ShouldBe(Money.FromDecimal(24_256.32m));
        list.Groups[1].Subtotal.ShouldBe(Money.FromDecimal(-2_974.61m));
        list.Total.ShouldBe(Money.FromDecimal(21_281.71m));
    }

    [Fact]
    public void Accounts_sort_by_their_manual_order_then_by_name()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Summary("Zebra", AccountType.Checking, 0m, sortOrder: 1),
            Summary("Alpha", AccountType.Checking, 0m, sortOrder: 2),
            Summary("Beta", AccountType.Checking, 0m, sortOrder: 2),
        ]);

        list.Groups[0].Accounts.Select(a => a.Name).ShouldBe(["Zebra", "Alpha", "Beta"]);
    }

    [Fact]
    public void Closed_accounts_sink_to_the_bottom_of_their_group()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Summary("Closed card", AccountType.CreditCard, 0m, sortOrder: 1, isClosed: true),
            Summary("Open card", AccountType.CreditCard, -50m, sortOrder: 2),
        ]);

        list.Groups[0].Accounts.Select(a => a.Name).ShouldBe(["Open card", "Closed card"]);
    }

    [Fact]
    public void The_cleared_total_is_tracked_separately_from_the_current_total()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Summary("Checking", AccountType.Checking, current: 850m, cleared: 900m),
            Summary("Card", AccountType.CreditCard, current: -100m, cleared: -80m),
        ]);

        list.Total.ShouldBe(Money.FromDecimal(750m));
        list.ClearedTotal.ShouldBe(Money.FromDecimal(820m));
    }

    [Fact]
    public void Imported_accounts_that_are_not_modelled_get_their_own_heading()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Summary("Checking", AccountType.Checking, 100m),
            Summary("Woodgrove Mortgage", AccountType.UnsupportedImported, -184_500m),
        ]);

        list.Groups.Select(g => g.Header).ShouldBe(["Bank accounts", "Other accounts"]);
        list.Groups[1].Accounts.Single().Account.IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_book_produces_an_empty_list()
    {
        AccountListSummary list = AccountListBuilder.Build([]);

        list.Groups.ShouldBeEmpty();
        list.Total.ShouldBe(Money.Zero);
        list.AccountCount.ShouldBe(0);
        list.AllAccounts.ShouldBeEmpty();
    }

    [Fact]
    public void The_uncategorized_count_adds_up_across_every_account()
    {
        AccountListSummary list = AccountListBuilder.Build(
        [
            Summary("Checking", AccountType.Checking, 0m, uncategorized: 12),
            Summary("Card", AccountType.CreditCard, 0m, uncategorized: 15),
        ]);

        list.UncategorizedCount.ShouldBe(27);
    }

    private static AccountSummary Summary(
        string name,
        AccountType type,
        decimal current = 0m,
        decimal? cleared = null,
        int sortOrder = 0,
        bool isClosed = false,
        int uncategorized = 0) =>
        new()
        {
            Account = new Account
            {
                Name = name,
                Type = type,
                SortOrder = sortOrder,
                IsClosed = isClosed,
            },
            CurrentBalance = Money.FromDecimal(current),
            ClearedBalance = Money.FromDecimal(cleared ?? current),
            TransactionCount = 0,
            UncategorizedCount = uncategorized,
        };
}
