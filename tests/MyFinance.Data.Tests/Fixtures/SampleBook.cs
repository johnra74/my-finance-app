using System.Globalization;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Mny;

namespace MyFinance.Data.Tests.Fixtures;

/// <summary>
/// A whole invented Money book — accounts, categories, payees, four years of transactions,
/// transfers, splits, recurring bills and holdings — in the shape <see cref="MoneyReader"/>
/// produces.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the end-to-end migration invariants — every balance survives, every
/// transaction has splits that add up, both legs of every transfer point at each other — run
/// on <em>every</em> machine. They used to run only where a real <c>.mny</c> happened to be
/// sitting, which meant the checks that matter most were the ones most often skipped, and it
/// put the pressure on to assert something about a real book, which is the one thing a
/// committed test must never do (constitution 8).
/// </para>
/// <para>
/// <b>Every name here is invented</b> and every figure is generated from a fixed seed. The
/// merchants are Microsoft's sample-database companies precisely because nobody banks with
/// them. Deterministic, so a failure reproduces; generated rather than typed, so the book is
/// big enough for the arithmetic to be worth checking.
/// </para>
/// </remarks>
internal static class SampleBook
{
    // Money's own identifiers, in the ranges the reader would hand back. Spread out so a
    // mistaken lookup against the wrong map fails rather than quietly finding something.
    public const int Checking = 1;
    public const int Savings = 2;
    public const int Card = 3;
    public const int ClosedCard = 4;
    public const int Brokerage = 5;

    private const int IncomeRoot = 100;
    private const int ExpenseRoot = 101;
    private const int Salary = 110;
    private const int Interest = 111;
    private const int Home = 120;
    private const int Rent = 121;
    private const int Utilities = 122;
    private const int Food = 130;
    private const int Groceries = 131;
    private const int Restaurants = 132;
    private const int Transport = 140;
    private const int Transit = 141;
    private const int Fuel = 142;
    private const int Health = 150;
    private const int Fun = 160;

    private const int Employer = 200;
    private const int Grocer = 201;
    private const int FuelStation = 202;
    private const int Utility = 203;
    private const int Landlord = 204;
    private const int Diner = 205;
    private const int Pharmacy = 206;
    private const int Cinema = 207;
    private const int TransitAuthority = 208;
    private const int CoffeeShop = 209;
    private const int Insurer = 210;

    // Money's hbillHead: the series key it stamps on every instance a bill generated.
    private const int SalaryHead = 900;
    private const int RentHead = 901;
    private const int UtilityHead = 902;
    private const int InsuranceHead = 903;
    private const int TransitHead = 904;

    private static readonly DateOnly FirstMonth = new(2022, 1, 1);
    private const int Months = 48;

    /// <summary>Builds the book. The same seed always builds the same book.</summary>
    public static MoneyBook Create(int seed = 20260907)
    {
        var random = new Random(seed);
        var transactions = new List<MoneyTransaction>();
        int nextId = 1000;

        int Id() => nextId++;

        // Rounded to the cent here rather than at the call site, because a fraction of a cent
        // surviving into an amount would make the balance checks fail for a reason that has
        // nothing to do with migration.
        decimal Vary(decimal around, decimal spread) =>
            Math.Round(around + ((decimal)random.NextDouble() * 2m * spread) - spread, 2);

        for (int month = 0; month < Months; month++)
        {
            DateOnly start = FirstMonth.AddMonths(month);

            DateOnly On(int day) => new(start.Year, start.Month, day);

            // -- income ------------------------------------------------------------------
            transactions.Add(Row(
                Id(), Checking, On(25), Vary(4200m, 60m),
                category: Salary, payee: Employer, head: SalaryHead,
                memo: "Salary", cleared: ClearedStatus.Reconciled));

            if (month % 3 == 0)
            {
                transactions.Add(Row(
                    Id(), Savings, On(28), Vary(6.40m, 1.20m),
                    category: Interest, memo: "Interest paid"));
            }

            // -- bills the schedule generated --------------------------------------------
            transactions.Add(Row(
                Id(), Checking, On(1), -1450m,
                category: Rent, payee: Landlord, head: RentHead,
                number: (1040 + month).ToString(CultureInfo.InvariantCulture),
                cleared: ClearedStatus.Reconciled));

            transactions.Add(Row(
                Id(), Checking, On(12), -Vary(138m, 42m),
                category: Utilities, payee: Utility, head: UtilityHead));

            if (month % 3 == 0)
            {
                transactions.Add(Row(
                    Id(), Checking, On(5), -312.75m,
                    category: Home, payee: Insurer, head: InsuranceHead, memo: "Contents cover"));
            }

            transactions.Add(Row(
                Id(), Checking, On(1), -45m,
                category: Transit, payee: TransitAuthority, head: TransitHead));

            transactions.Add(Row(
                Id(), Checking, On(16), -45m,
                category: Transit, payee: TransitAuthority, head: TransitHead));

            // -- day to day, on the card -------------------------------------------------
            int[] groceryDays = [3, 10, 17, 24];

            foreach (int day in groceryDays)
            {
                transactions.Add(Row(
                    Id(), Card, On(day), -Vary(94m, 38m), category: Groceries, payee: Grocer));
            }

            int[] fuelDays = [8, 22];

            foreach (int day in fuelDays)
            {
                transactions.Add(Row(
                    Id(), Card, On(day), -Vary(58m, 16m), category: Fuel, payee: FuelStation));
            }

            transactions.Add(Row(
                Id(), Card, On(19), -Vary(46m, 18m), category: Restaurants, payee: Diner));

            int[] coffeeDays = [6, 20];

            foreach (int day in coffeeDays)
            {
                transactions.Add(Row(
                    Id(), Card, On(day), -Vary(9.20m, 3m), category: Restaurants, payee: CoffeeShop));
            }

            // -- a split, once a quarter -------------------------------------------------
            //
            // One trip, two categories. The parent carries the total and no category of its
            // own; the parts carry the breakdown. Getting this wrong is how a book ends up
            // with a transaction whose splits do not add up to it.
            if (month % 3 == 1)
            {
                decimal medicine = Vary(28m, 9m);
                decimal magazine = Vary(11m, 4m);
                int parent = Id();

                transactions.Add(Row(
                    parent, Card, On(14), -(medicine + magazine), payee: Pharmacy));

                transactions.Add(Row(
                    Id(), Card, On(14), -medicine,
                    category: Health, splitParent: parent, splitIndex: 0));

                transactions.Add(Row(
                    Id(), Card, On(14), -magazine,
                    category: Fun, splitParent: parent, splitIndex: 1, memo: "Magazine"));
            }

            // -- transfers, both legs ----------------------------------------------------
            AddTransfer(transactions, Id, Checking, Savings, On(26), 250m, "To savings");

            // Paying the card off: what leaves the current account arrives against the card
            // balance, which is why the two legs carry opposite signs.
            AddTransfer(transactions, Id, Checking, Card, On(28), Vary(520m, 120m), "Card payment");
        }

        // A pair of bills Money projected into the register but nobody ever entered. They are
        // real rows in a real file and the migration has to be able to leave them out.
        transactions.Add(Row(
            Id(), Checking, new DateOnly(2026, 1, 1), -1450m,
            category: Rent, payee: Landlord, head: RentHead, scheduled: true));

        transactions.Add(Row(
            Id(), Checking, new DateOnly(2026, 1, 12), -142m,
            category: Utilities, payee: Utility, head: UtilityHead, scheduled: true));

        // A closed account with a little history, because a book that has been kept for years
        // always has one and the account list has to still add up with it there.
        transactions.Add(Row(
            Id(), ClosedCard, new DateOnly(2022, 2, 4), -76.40m,
            category: Fun, payee: Cinema));

        transactions.Add(Row(
            Id(), ClosedCard, new DateOnly(2022, 3, 9), 76.40m, memo: "Balance settled"));

        return new MoneyBook
        {
            Accounts = Accounts(),
            Categories = Categories(),
            Payees = Payees(),
            Transactions = transactions,
            Scheduled = Bills(transactions),
            Securities = Securities(),
            Holdings =
            [
                new MoneyHolding(Brokerage, 300, 120.5m),
                new MoneyHolding(Brokerage, 301, 42m),
            ],
            SecurityPrices =
            [
                new MoneySecurityPrice(300, new DateOnly(2025, 12, 31), 31.42m),
                new MoneySecurityPrice(301, new DateOnly(2025, 12, 31), 88.10m),
            ],
            MerchantCodes = [new MoneyMerchantCode("5411", Groceries)],
            Diagnostics = [],
        };
    }

    private static IReadOnlyList<MoneyAccount> Accounts() =>
    [
        new(Checking, "Everyday Checking", AccountType.Checking, AccountGroup.Bank,
            false, true, Money.FromDecimal(1250m), new DateOnly(2019, 6, 1), null, "••4417", "Contoso Bank"),
        new(Savings, "Rainy Day Savings", AccountType.Savings, AccountGroup.Bank,
            false, false, Money.FromDecimal(5000m), new DateOnly(2019, 6, 1), null, "••8802", "Contoso Bank"),
        new(Card, "Everyday Rewards Card", AccountType.CreditCard, AccountGroup.Credit,
            false, true, Money.FromDecimal(-320m), new DateOnly(2020, 2, 14),
            Money.FromDecimal(6000m), "••1195", "Fabrikam Card Services"),
        new(ClosedCard, "Old Store Card", AccountType.CreditCard, AccountGroup.Credit,
            true, false, Money.Zero, new DateOnly(2016, 9, 3), Money.FromDecimal(1500m), null, null),
        new(Brokerage, "Retirement Brokerage", AccountType.Brokerage, AccountGroup.Other,
            false, false, Money.Zero, new DateOnly(2018, 1, 8), null, null, "Northwind Securities"),
    ];

    private static IReadOnlyList<MoneyCategory> Categories() =>
    [
        // Money's two roots are a flag rather than a category, and migration drops them.
        new(IncomeRoot, "INCOME", null, 0, CategoryKind.Income),
        new(ExpenseRoot, "EXPENSE", null, 0, CategoryKind.Expense),

        new(Salary, "Salary", IncomeRoot, 1, CategoryKind.Income),
        new(Interest, "Interest", IncomeRoot, 1, CategoryKind.Income),

        new(Home, "Home", ExpenseRoot, 1, CategoryKind.Expense),
        new(Rent, "Rent", Home, 2, CategoryKind.Expense),
        new(Utilities, "Utilities", Home, 2, CategoryKind.Expense),
        new(Food, "Food", ExpenseRoot, 1, CategoryKind.Expense),
        new(Groceries, "Groceries", Food, 2, CategoryKind.Expense),
        new(Restaurants, "Restaurants", Food, 2, CategoryKind.Expense),
        new(Transport, "Transport", ExpenseRoot, 1, CategoryKind.Expense),
        new(Transit, "Transit", Transport, 2, CategoryKind.Expense),
        new(Fuel, "Fuel", Transport, 2, CategoryKind.Expense),
        new(Health, "Health", ExpenseRoot, 1, CategoryKind.Expense),
        new(Fun, "Fun", ExpenseRoot, 1, CategoryKind.Expense),
    ];

    private static IReadOnlyList<MoneyPayee> Payees() =>
    [
        new(Employer, "Northwind Traders", false),
        new(Grocer, "Contoso Grocers", false),
        new(FuelStation, "Fabrikam Fuel", false),
        new(Utility, "Lakeside Utilities", false),
        new(Landlord, "Blue Yonder Rentals", false),
        new(Diner, "Tailspin Diner", false),
        new(Pharmacy, "Wingtip Pharmacy", false),
        new(Cinema, "Proseware Cinema", true),
        new(TransitAuthority, "Adventure Works Transit", false),
        new(CoffeeShop, "Fourth Coffee", false),
        new(Insurer, "Litware Insurance", false),
    ];

    private static IReadOnlyList<MoneySecurity> Securities() =>
    [
        new(300, "Contoso Index Fund", "CIFX"),
        new(301, "Northwind Growth Fund", "NWGX"),
    ];

    /// <summary>
    /// The recurring series, each pointing at the last instance it generated.
    /// </summary>
    /// <remarks>
    /// Money keeps the recurrence on the bill row and everything else on a template
    /// transaction, so a bill without a template is meaningless. The last generated instance
    /// stands in for it here, which is the same shape the reader hands over.
    /// </remarks>
    private static IReadOnlyList<MoneyScheduled> Bills(List<MoneyTransaction> transactions)
    {
        int Template(int head) => transactions.Last(t => t.ScheduleHeadId == head).Id;

        return
        [
            new(1, Template(SalaryHead), Checking, Employer, Money.FromDecimal(4200m),
                Salary, "Salary", new DateOnly(2022, 1, 25), RecurrenceFrequency.Monthly,
                null, null, 5, 4, 1, SalaryHead),

            new(2, Template(RentHead), Checking, Landlord, Money.FromDecimal(-1450m),
                Rent, null, new DateOnly(2022, 1, 1), RecurrenceFrequency.Monthly,
                null, null, 5, 4, 1, RentHead),

            new(3, Template(UtilityHead), Checking, Utility, Money.FromDecimal(-138m),
                Utilities, null, new DateOnly(2022, 1, 12), RecurrenceFrequency.Monthly,
                null, null, null, 4, 1, UtilityHead),

            new(4, Template(InsuranceHead), Checking, Insurer, Money.FromDecimal(-312.75m),
                Home, "Contents cover", new DateOnly(2022, 1, 5), RecurrenceFrequency.Quarterly,
                null, null, 10, 12, 1, InsuranceHead),

            // Twice a month, on the 1st and the 16th. The second day is a fortnight after the
            // first unless the file says otherwise, which is what makes those two the pair.
            new(5, Template(TransitHead), Checking, TransitAuthority, Money.FromDecimal(-45m),
                Transit, null, new DateOnly(2022, 1, 1), RecurrenceFrequency.TwiceAMonth,
                null, null, null, 8, 2, TransitHead),

            // A series whose repeat code has never been verified against Money's own screen.
            // It must be reported for the user to set up, never converted to something
            // plausible — see specs/014-scheduled-bill-migration.
            new(6, Template(SalaryHead), Checking, Cinema, Money.FromDecimal(-14.99m),
                Fun, "Streaming", new DateOnly(2022, 1, 20), null,
                null, null, null, 47, 1),
        ];
    }

    private static MoneyTransaction Row(
        int id,
        int account,
        DateOnly date,
        decimal amount,
        int? category = null,
        int? payee = null,
        string? number = null,
        string? memo = null,
        ClearedStatus cleared = ClearedStatus.Cleared,
        int? splitParent = null,
        int splitIndex = 0,
        bool scheduled = false,
        int? head = null) =>
        new(id, account, null, date, Money.FromDecimal(amount), category, payee, number, memo,
            cleared, false, false, splitParent, splitIndex, scheduled, head);

    /// <summary>
    /// Writes both legs of one transfer, the way Money records it.
    /// </summary>
    /// <remarks>
    /// Two rows, one in each account, opposite signs, each naming the other's account and
    /// neither carrying a category. A transfer is not spending, and giving one a category is
    /// how the same money ends up counted twice in a report.
    /// </remarks>
    private static void AddTransfer(
        List<MoneyTransaction> transactions,
        Func<int> id,
        int from,
        int to,
        DateOnly date,
        decimal amount,
        string memo)
    {
        transactions.Add(new MoneyTransaction(
            id(), from, to, date, Money.FromDecimal(-amount), null, null, null, memo,
            ClearedStatus.Reconciled, true, true, null, 0, false));

        transactions.Add(new MoneyTransaction(
            id(), to, from, date, Money.FromDecimal(amount), null, null, null, memo,
            ClearedStatus.Reconciled, true, false, null, 0, false));
    }
}
