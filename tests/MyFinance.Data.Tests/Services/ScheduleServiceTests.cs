using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Scheduling;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

public sealed class ScheduleServiceTests
{
    private static readonly DateOnly Today = new(2026, 8, 23);

    [Fact]
    public async Task A_bill_can_be_scheduled_and_read_back()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Everyday Checking");

        int id = await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Lakeside Broadband",
            Amount = Money.FromDecimal(-44.99m),
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = new DateOnly(2026, 9, 7),
            PaymentMethod = PaymentMethod.WriteCheck,
        });

        BillListItem bill = (await book.Schedules.GetBillsAsync(Today)).Bills.Single();

        bill.Id.ShouldBe(id);
        bill.PayeeName.ShouldBe("Lakeside Broadband");
        bill.AccountName.ShouldBe("Everyday Checking");
        bill.Amount.ShouldBe(Money.FromDecimal(-44.99m));
        bill.NextDue.ShouldBe(new DateOnly(2026, 9, 7));
        bill.FrequencyText.ShouldBe("Monthly");
        bill.PaymentMethodText.ShouldBe("Write Check");
        bill.IsOverdue.ShouldBeFalse();
    }

    [Fact]
    public async Task A_bill_with_no_amount_is_refused_unless_it_is_an_estimate()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Schedules.CreateAsync(new ScheduleDraft
            {
                AccountId = account,
                PayeeName = "Nothing",
                Amount = Money.Zero,
            }));

        thrown.Errors.ShouldContain(e => e.Code == ScheduleService.AmountRequired);

        // An estimate is allowed to start at zero: the point of one is that the figure is
        // not known yet.
        await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Electricity",
            Amount = Money.Zero,
            IsEstimate = true,
        });
    }

    [Fact]
    public async Task Categories_that_do_not_add_up_to_the_bill_are_refused()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int rent = await book.CategoryIdAsync("Bills : Rent");

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Schedules.CreateAsync(new ScheduleDraft
            {
                AccountId = account,
                PayeeName = "Landlord",
                Amount = Money.FromDecimal(-1_200m),
                Splits = [new SplitDraft { CategoryId = rent, Amount = Money.FromDecimal(-1_000m) }],
            }));

        thrown.Errors.ShouldContain(e => e.Code == ScheduleService.SplitsDoNotSum);
    }

    [Fact]
    public async Task An_overdue_bill_reports_how_far_behind_it_is()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Everyday Checking");

        // A card bill due on 17 April and read on 23 August: months of occurrences behind.
        await book.AddBillAsync(account, "Fabrikam Card Services", -220.45m, new DateOnly(2026, 4, 17));

        BillListItem bill = (await book.Schedules.GetBillsAsync(Today)).Bills.Single();

        bill.IsOverdue.ShouldBeTrue();
        bill.DaysOverdue.ShouldBe(128);
        bill.OverdueCount.ShouldBe(5);
        bill.OverdueText.ShouldBe("This transaction is 128 days overdue (5 occurrences past due).");
    }

    [Fact]
    public async Task A_twice_monthly_deposit_counts_its_own_backlog()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Everyday Checking");

        // A salary paid on the 15th and the 30th, which is what twice-a-month has to mean.
        await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Northwind Traders",
            Amount = Money.FromDecimal(2_450m),
            IsEstimate = true,
            Frequency = RecurrenceFrequency.TwiceAMonth,
            StartDate = new DateOnly(2026, 4, 15),
            SecondDayOfMonth = 30,
            PaymentMethod = PaymentMethod.DirectDeposit,
        });

        BillListItem bill = (await book.Schedules.GetBillsAsync(Today)).Bills.Single();

        bill.DaysOverdue.ShouldBe(130);
        bill.OverdueCount.ShouldBe(9);
        bill.OverdueText.ShouldBe("This transaction is 130 days overdue (9 occurrences past due).");
        bill.FrequencyText.ShouldBe("Twice a month");
        bill.PaymentMethodText.ShouldBe("Direct Deposit");
        bill.IsEstimate.ShouldBeTrue();
    }

    [Fact]
    public async Task Overdue_bills_come_first_then_the_rest_by_due_date()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.AddBillAsync(account, "Later", -10m, new DateOnly(2026, 9, 7));
        await book.AddBillAsync(account, "Sooner", -20m, new DateOnly(2026, 8, 28));
        await book.AddBillAsync(account, "Overdue", -30m, new DateOnly(2026, 4, 20));

        IReadOnlyList<BillListItem> bills = (await book.Schedules.GetBillsAsync(Today)).Bills;

        bills.Select(b => b.PayeeName).ShouldBe(["Overdue", "Sooner", "Later"]);
    }

    [Fact]
    public async Task Entering_a_bill_writes_it_into_the_register_on_the_day_it_was_due()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int id = await book.AddBillAsync(account, "Landlord", -1_200m, new DateOnly(2026, 8, 1));

        int transactionId = await book.Schedules.EnterNextAsync(id, Today);

        Transaction written = (await book.ReadTransactionAsync(transactionId))!;

        written.Date.ShouldBe(new DateOnly(2026, 8, 1));
        written.Amount.ShouldBe(Money.FromDecimal(-1_200m));
        written.ScheduledTransactionId.ShouldBe(id);

        // Entered from a schedule, not seen on a statement — the bank has not acknowledged
        // it yet.
        written.ClearedStatus.ShouldBe(ClearedStatus.Uncleared);

        (await book.Register.GetRegisterAsync(account)).CurrentBalance
            .ShouldBe(Money.FromDecimal(3_800m));
    }

    [Fact]
    public async Task An_entered_bill_stops_being_overdue_and_moves_to_the_next_date()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int id = await book.AddBillAsync(account, "Rent", -1_200m, new DateOnly(2026, 7, 1));

        BillListItem before = (await book.Schedules.GetBillsAsync(Today)).Bills.Single();
        before.OverdueCount.ShouldBe(2);

        await book.Schedules.EnterNextAsync(id, Today);

        BillListItem after = (await book.Schedules.GetBillsAsync(Today)).Bills.Single();
        after.OverdueCount.ShouldBe(1);
        after.NextDue.ShouldBe(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task Entering_the_same_bill_twice_does_not_record_it_twice()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int id = await book.AddBillAsync(account, "Rent", -1_200m, new DateOnly(2026, 8, 1));

        await book.Schedules.EnterNextAsync(id, Today);
        await book.Schedules.EnterNextAsync(id, Today);

        // The second call advances to the following due date rather than repeating August.
        await using MyFinanceDbContext db = book.CreateContext();
        List<DateOnly> dates = await db.Transactions.Select(t => t.Date).ToListAsync();

        dates.Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_backlog_can_be_entered_in_one_action_with_each_on_its_own_day()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 10_000m);
        int id = await book.AddBillAsync(account, "Rent", -1_200m, new DateOnly(2026, 5, 1));

        EnterResult result = await book.Schedules.EnterAllDueAsync(id, Today);

        // May through August is four payments.
        result.Entered.ShouldBe(4);
        result.Total.ShouldBe(Money.FromDecimal(-4_800m));

        RegisterView view = await book.Register.GetRegisterAsync(account);
        view.CurrentBalance.ShouldBe(Money.FromDecimal(5_200m));

        // Each posted on the day it was owed, so the running balance stays truthful about
        // when the money actually left.
        view.Lines.Select(l => l.Transaction.Date).ShouldBe(
        [
            new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1),
        ]);

        (await book.Schedules.GetBillsAsync(Today)).Bills.Single().IsOverdue.ShouldBeFalse();
    }

    [Fact]
    public async Task Skipping_clears_an_occurrence_without_writing_anything()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int id = await book.AddBillAsync(account, "Gym", -40m, new DateOnly(2026, 7, 1));

        DateOnly skipped = await book.Schedules.SkipNextAsync(id, Today);

        skipped.ShouldBe(new DateOnly(2026, 7, 1));

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Transactions.CountAsync()).ShouldBe(0);

        BillListItem bill = (await book.Schedules.GetBillsAsync(Today)).Bills.Single();
        bill.NextDue.ShouldBe(new DateOnly(2026, 8, 1));
        bill.OverdueCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_category_template_is_copied_onto_the_generated_transaction()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int rent = await book.CategoryIdAsync("Bills : Rent");

        int id = await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Landlord",
            Amount = Money.FromDecimal(-1_200m),
            StartDate = new DateOnly(2026, 8, 1),
            Splits = [new SplitDraft { CategoryId = rent, Amount = Money.FromDecimal(-1_200m) }],
        });

        int transactionId = await book.Schedules.EnterNextAsync(id, Today);

        Transaction written = (await book.Register.FindAsync(transactionId))!;
        written.Splits.Single().CategoryId.ShouldBe(rent);
    }

    [Fact]
    public async Task A_different_actual_amount_is_taken_by_a_single_category()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int electricity = await book.CategoryIdAsync("Bills : Electricity");

        int id = await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "PSE&G",
            Amount = Money.FromDecimal(-200m),
            IsEstimate = true,
            StartDate = new DateOnly(2026, 8, 1),
            Splits = [new SplitDraft { CategoryId = electricity, Amount = Money.FromDecimal(-200m) }],
        });

        int transactionId = await book.Schedules.EnterNextAsync(
            id, Today, actualAmount: Money.FromDecimal(-243.17m));

        Transaction written = (await book.Register.FindAsync(transactionId))!;

        written.Amount.ShouldBe(Money.FromDecimal(-243.17m));
        written.Splits.Single().Amount.ShouldBe(Money.FromDecimal(-243.17m));
        written.Splits.Single().CategoryId.ShouldBe(electricity);
    }

    [Fact]
    public async Task A_different_actual_amount_on_a_split_bill_goes_to_the_first_line()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int rent = await book.CategoryIdAsync("Bills : Rent");
        int water = await book.CategoryIdAsync("Bills : Water");

        int id = await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Landlord",
            Amount = Money.FromDecimal(-1_200m),
            StartDate = new DateOnly(2026, 8, 1),
            Splits =
            [
                new SplitDraft { CategoryId = rent, Amount = Money.FromDecimal(-1_100m) },
                new SplitDraft { CategoryId = water, Amount = Money.FromDecimal(-100m) },
            ],
        });

        int transactionId = await book.Schedules.EnterNextAsync(
            id, Today, actualAmount: Money.FromDecimal(-1_250m));

        Transaction written = (await book.Register.FindAsync(transactionId))!;

        // There is no way to know which of several categories a variation belongs to, so it
        // goes on the first line rather than being spread by a guess.
        written.Splits.OrderBy(s => s.SortOrder).First().Amount.ShouldBe(Money.FromDecimal(-1_150m));
        Money.Sum(written.Splits.Select(s => s.Amount)).ShouldBe(Money.FromDecimal(-1_250m));
    }

    [Fact]
    public async Task Auto_entry_only_touches_series_that_asked_for_it()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);

        await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Automatic",
            Amount = Money.FromDecimal(-100m),
            StartDate = new DateOnly(2026, 8, 1),
            AutoEnter = true,
        });

        await book.AddBillAsync(account, "Manual", -200m, new DateOnly(2026, 8, 1));

        EnterResult result = await book.Schedules.AutoEnterDueAsync(Today);

        // Writing to the register unasked has to be opted into, once, deliberately.
        result.Entered.ShouldBe(1);

        Transaction written = (await book.Register.GetRegisterAsync(account)).Lines.Single().Transaction;
        written.Amount.ShouldBe(Money.FromDecimal(-100m));
    }

    [Fact]
    public async Task Auto_entry_reaches_forward_by_the_days_ahead_setting()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);

        await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Soon",
            Amount = Money.FromDecimal(-100m),
            StartDate = Today.AddDays(3),
            AutoEnter = true,
            DaysAheadToEnter = 5,
        });

        await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Later",
            Amount = Money.FromDecimal(-200m),
            StartDate = Today.AddDays(20),
            AutoEnter = true,
            DaysAheadToEnter = 5,
        });

        (await book.Schedules.AutoEnterDueAsync(Today)).Entered.ShouldBe(1);
    }

    [Fact]
    public async Task Running_auto_entry_twice_does_not_double_up()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);

        await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Rent",
            Amount = Money.FromDecimal(-1_200m),
            StartDate = new DateOnly(2026, 8, 1),
            AutoEnter = true,
        });

        await book.Schedules.AutoEnterDueAsync(Today);
        EnterResult second = await book.Schedules.AutoEnterDueAsync(Today);

        // Opening the book twice in a day must not pay the rent twice.
        second.Entered.ShouldBe(0);
        (await book.Register.GetRegisterAsync(account)).Lines.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_schedule_leaves_the_transactions_it_produced()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int id = await book.AddBillAsync(account, "Rent", -1_200m, new DateOnly(2026, 8, 1));

        await book.Schedules.EnterNextAsync(id, Today);
        await book.Schedules.DeleteAsync(id);

        // The money genuinely left the account, whatever happened to the schedule that
        // predicted it.
        (await book.Register.GetRegisterAsync(account)).Lines.Count.ShouldBe(1);
        (await book.Schedules.GetBillsAsync(Today)).Bills.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_inactive_bill_drops_off_the_list_without_being_lost()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int id = await book.AddBillAsync(account, "Old subscription", -10m, new DateOnly(2026, 8, 1));

        await book.Schedules.SetActiveAsync(id, isActive: false);

        (await book.Schedules.GetBillsAsync(Today)).Bills.ShouldBeEmpty();
        (await book.Schedules.GetBillsAsync(Today, includeInactive: true)).Bills.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Moving_the_pattern_does_not_disturb_what_has_already_been_paid()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int id = await book.AddBillAsync(account, "Rent", -1_200m, new DateOnly(2026, 7, 1));

        await book.Schedules.EnterNextAsync(id, Today);

        await book.Schedules.UpdateAsync(new ScheduleDraft
        {
            Id = id,
            AccountId = account,
            PayeeName = "Rent",
            Amount = Money.FromDecimal(-1_300m),
            StartDate = new DateOnly(2026, 7, 15),
        });

        // The July payment stands; only the future dates move.
        (await book.Register.GetRegisterAsync(account)).Lines.Count.ShouldBe(1);

        BillListItem bill = (await book.Schedules.GetBillsAsync(Today)).Bills.Single();
        bill.Amount.ShouldBe(Money.FromDecimal(-1_300m));
        bill.NextDue.ShouldBe(new DateOnly(2026, 7, 15));
    }

    [Fact]
    public async Task Bills_cannot_be_scheduled_against_an_imported_balance_only_account()
    {
        using var book = new BookHarness();
        int mortgage = await book.AddAccountAsync("Woodgrove Mortgage", AccountType.UnsupportedImported);

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.AddBillAsync(mortgage, "Payment", -1_000m, new DateOnly(2026, 8, 1)));

        thrown.Errors.ShouldContain(e => e.Code == ScheduleService.AccountReadOnly);
    }

    [Fact]
    public async Task The_history_records_what_happened_to_each_occurrence()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int id = await book.AddBillAsync(account, "Rent", -1_200m, new DateOnly(2026, 6, 1));

        await book.Schedules.EnterNextAsync(id, Today);
        await book.Schedules.SkipNextAsync(id, Today);

        IReadOnlyList<OccurrenceListItem> history = await book.Schedules.GetHistoryAsync(id);

        history.Count.ShouldBe(2);
        history.ShouldContain(o => o.State == ScheduleOccurrenceState.Entered);
        history.ShouldContain(o => o.State == ScheduleOccurrenceState.Skipped);
    }
}

public sealed class CashFlowForecastTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);

    [Fact]
    public async Task The_forecast_projects_scheduled_bills_forward()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 3_000m);

        await book.AddBillAsync(account, "Rent", -1_200m, new DateOnly(2026, 9, 5));
        await book.AddBillAsync(account, "Salary", 4_000m, new DateOnly(2026, 9, 25));

        CashFlowProjection projection = await book.Schedules.ForecastAsync(
            account, Today, new DateOnly(2026, 9, 30));

        projection.OpeningBalance.ShouldBe(Money.FromDecimal(3_000m));
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(5_800m));
        projection.GoesNegative.ShouldBeFalse();
    }

    [Fact]
    public async Task The_forecast_finds_the_point_where_the_account_would_go_short()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 500m);

        await book.AddBillAsync(account, "Mortgage", -2_000m, new DateOnly(2026, 9, 3));
        await book.AddBillAsync(account, "Salary", 4_000m, new DateOnly(2026, 9, 25));

        CashFlowProjection projection = await book.Schedules.ForecastAsync(
            account, Today, new DateOnly(2026, 9, 30));

        // Ends the month comfortably and is overdrawn in the middle of it, which is the
        // whole reason to project rather than to total.
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(2_500m));
        projection.GoesNegative.ShouldBeTrue();
        projection.FirstNegativeDate.ShouldBe(new DateOnly(2026, 9, 3));
    }

    [Fact]
    public async Task A_bill_already_entered_is_not_projected_again()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 3_000m);
        int id = await book.AddBillAsync(account, "Rent", -1_200m, new DateOnly(2026, 9, 5));

        await book.Schedules.EnterNextAsync(id, new DateOnly(2026, 9, 6));

        CashFlowProjection projection = await book.Schedules.ForecastAsync(
            account, Today, new DateOnly(2026, 9, 30));

        // Counting it again as a scheduled item would show the same payment leaving twice.
        projection.Expected.ShouldBeEmpty();

        // It still moves the line, because the money genuinely left the account inside the
        // window even though the opening balance was taken before it.
        projection.Recorded.Count().ShouldBe(1);
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(1_800m));
    }

    [Fact]
    public async Task The_opening_balance_is_taken_at_the_start_of_the_window()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        await book.AddTransactionAsync(account, -300m, new DateOnly(2026, 8, 15));
        await book.AddTransactionAsync(account, -400m, new DateOnly(2026, 9, 15));

        CashFlowProjection projection = await book.Schedules.ForecastAsync(
            account, Today, new DateOnly(2026, 9, 30));

        // Only what had happened before 1 September sets the opening figure.
        projection.OpeningBalance.ShouldBe(Money.FromDecimal(700m));

        // The September transaction is inside the window, so it moves the line rather than
        // disappearing between the opening balance and the scheduled items.
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(300m));
    }

    [Fact]
    public async Task A_forecast_across_every_account_adds_them_together()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings, openingBalance: 5_000m);

        await book.AddBillAsync(checking, "Rent", -1_200m, new DateOnly(2026, 9, 5));

        CashFlowProjection projection = await book.Schedules.ForecastAsync(
            null, Today, new DateOnly(2026, 9, 30));

        projection.OpeningBalance.ShouldBe(Money.FromDecimal(6_000m));
        projection.ClosingBalance.ShouldBe(Money.FromDecimal(4_800m));
    }

    [Fact]
    public async Task Estimates_are_carried_through_so_the_projection_can_say_so()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        await book.Schedules.CreateAsync(new ScheduleDraft
        {
            AccountId = account,
            PayeeName = "Electricity",
            Amount = Money.FromDecimal(-120m),
            IsEstimate = true,
            StartDate = new DateOnly(2026, 9, 10),
        });

        CashFlowProjection projection = await book.Schedules.ForecastAsync(
            account, Today, new DateOnly(2026, 9, 30));

        projection.IncludesEstimates.ShouldBeTrue();
    }
}
