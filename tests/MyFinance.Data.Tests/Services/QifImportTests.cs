using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Import.Categorization;

namespace MyFinance.Data.Tests.Services;

public sealed class QifImportTests
{
    [Fact]
    public async Task A_qif_export_loads_into_the_register()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        ImportSummary summary = await book.ImportQifAsync(account, """
            !Type:Bank
            D03/02/2026
            T-18.40
            PBlue Bottle Coffee
            ^
            D03/05/2026
            T-250.00
            PLandlord
            ^
            """);

        summary.Added.ShouldBe(2);

        RegisterView view = await book.Register.GetRegisterAsync(account);
        view.CurrentBalance.ShouldBe(Money.FromDecimal(731.60m));
    }

    [Fact]
    public async Task The_files_own_categories_are_used_when_the_book_already_has_them()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        ImportPreview preview = await book.Import.PrepareAsync(
            book.ParseQif("""
                !Type:Bank
                D03/02/2026
                T-18.40
                PBlue Bottle
                LFood:Coffee
                ^
                """),
            account);

        // Quicken writes "Food:Coffee" where this application renders "Food : Coffee", so
        // the match is made on the words rather than the punctuation.
        preview.Candidates[0].SuggestedCategoryId.ShouldBe(coffee);
        preview.Candidates[0].Suggestion.Source.ShouldBe(SuggestionSource.FileCategory);

        await book.Import.CommitAsync(preview, new ImportRequest { AccountId = account, Rows = [] });

        Transaction imported = (await book.Register.GetRegisterAsync(account)).Lines[0].Transaction;
        imported.Splits.Single().CategoryId.ShouldBe(coffee);
    }

    [Fact]
    public async Task A_category_the_book_lacks_is_reported_rather_than_invented()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportPreview preview = await book.Import.PrepareAsync(
            book.ParseQif("""
                !Type:Bank
                D03/02/2026
                T-18.40
                PSomething
                LHobbies:Model trains
                ^
                """),
            account);

        preview.MissingCategoryPaths.ShouldBe(["Hobbies : Model trains"]);
        preview.Candidates[0].SuggestedCategoryId.ShouldBeNull();
    }

    [Fact]
    public async Task Missing_categories_are_created_when_the_user_asks()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportSummary summary = await book.ImportQifAsync(account, """
            !Type:Bank
            D03/02/2026
            T-18.40
            PSomething
            LHobbies:Model trains
            ^
            """,
            createMissingCategories: true);

        summary.CategoriesCreated.ShouldBeGreaterThan(0);

        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();
        categories.ShouldContain(c => c.FullName == "Hobbies : Model trains");

        // The row lands in the category the file named, which is the whole point of carrying
        // a ledger across from another program.
        Transaction imported = (await book.Register.GetRegisterAsync(account)).Lines[0].Transaction;
        imported.Splits.Single().CategoryId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Creating_a_category_reuses_a_heading_that_already_exists()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.ImportQifAsync(account, """
            !Type:Bank
            D03/02/2026
            T-18.40
            PSomething
            LFood:Street food
            ^
            """,
            createMissingCategories: true);

        IReadOnlyList<CategoryListItem> categories = await book.Categories.GetAllAsync();

        // "Food" already exists, so only the subcategory is new — a second "Food" heading
        // would split every food report in two.
        categories.Count(c => c.FullName == "Food").ShouldBe(1);
        categories.ShouldContain(c => c.FullName == "Food : Street food");
    }

    [Fact]
    public async Task The_cleared_state_from_the_file_is_honoured_row_by_row()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.ImportQifAsync(account, """
            !Type:Bank
            D03/02/2026
            T-18.40
            PReconciled item
            CX
            ^
            D03/03/2026
            T-20.00
            PCleared item
            C*
            ^
            D03/04/2026
            T-30.00
            PUnstated item
            ^
            """);

        List<Transaction> rows =
        [
            .. (await book.Register.GetRegisterAsync(account)).Lines.Select(l => l.Transaction)
        ];

        // A row the exporting program had reconciled is not merely cleared, and flattening
        // the distinction would quietly undo work already agreed with the bank.
        rows[0].ClearedStatus.ShouldBe(ClearedStatus.Reconciled);
        rows[1].ClearedStatus.ShouldBe(ClearedStatus.Cleared);

        // Nothing stated falls back to cleared, as for OFX: it is historical data that has
        // long since reached the bank.
        rows[2].ClearedStatus.ShouldBe(ClearedStatus.Cleared);
    }

    [Fact]
    public async Task Split_lines_in_the_file_arrive_as_a_split_transaction()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int groceries = await book.CategoryIdAsync("Food : Groceries");
        int household = await book.CategoryIdAsync("Home : Household supplies");

        await book.ImportQifAsync(account, """
            !Type:Bank
            D03/02/2026
            T-142.83
            PCostco
            SFood:Groceries
            EWeekly shop
            $-118.20
            SHome:Household supplies
            EDetergent
            $-24.63
            ^
            """);

        Transaction imported = (await book.Register.GetRegisterAsync(account)).Lines[0].Transaction;

        // Flattening this back into one uncategorized row would discard exactly the work the
        // user spent time recording in the program they are leaving.
        imported.IsSplit.ShouldBeTrue();
        imported.Splits.Count.ShouldBe(2);

        TransactionSplit first = imported.Splits.OrderBy(s => s.SortOrder).First();
        first.CategoryId.ShouldBe(groceries);
        first.Memo.ShouldBe("Weekly shop");
        first.Amount.ShouldBe(Money.FromDecimal(-118.20m));

        imported.Splits.Single(s => s.CategoryId == household).Amount
            .ShouldBe(Money.FromDecimal(-24.63m));

        Money.Sum(imported.Splits.Select(s => s.Amount)).ShouldBe(imported.Amount);
    }

    [Fact]
    public async Task A_split_whose_categories_the_book_lacks_can_still_be_brought_across()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.ImportQifAsync(account, """
            !Type:Bank
            D03/02/2026
            T-100.00
            PHobby shop
            SHobbies:Model trains
            $-60.00
            SHobbies:Paint
            $-40.00
            ^
            """,
            createMissingCategories: true);

        Transaction imported = (await book.Register.GetRegisterAsync(account)).Lines[0].Transaction;

        imported.Splits.Count.ShouldBe(2);
        imported.Splits.ShouldAllBe(s => s.CategoryId != null);
    }

    [Fact]
    public async Task Re_importing_a_qif_file_flags_its_rows_as_probable_duplicates()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        const string file = """
            !Type:Bank
            D03/02/2026
            T-18.40
            PBlue Bottle
            ^
            """;

        await book.ImportQifAsync(account, file);

        ImportPreview second = await book.Import.PrepareAsync(book.ParseQif(file), account);

        // QIF has no bank reference, so this can only ever be approximate — matched on the
        // amount, a nearby date and the payee.
        second.Candidates[0].Duplicate.Kind.ShouldBe(
            MyFinance.Import.Dedupe.DuplicateKind.Likely);
        second.Candidates[0].IncludedByDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task A_transfer_marked_in_the_file_arrives_as_a_plain_transaction()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 1_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        await book.ImportQifAsync(checking, """
            !Type:Bank
            D03/02/2026
            T-400.00
            PTransfer
            L[Savings]
            ^
            """);

        // Inventing the far leg would create a transaction in an account the user never
        // imported, and would then be duplicated when they import its own export.
        (await book.Register.GetRegisterAsync(checking)).CurrentBalance
            .ShouldBe(Money.FromDecimal(600m));
        (await book.Register.GetRegisterAsync(savings)).CurrentBalance.ShouldBe(Money.Zero);
    }
}

public sealed class SuggestionPipelineTests
{
    [Fact]
    public async Task A_rule_decides_the_category_ahead_of_everything_else()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int fuel = await book.CategoryIdAsync("Transport : Fuel");
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        // Payee memory would say coffee; the rule says fuel and must win.
        await book.AddTransactionAsync(account, -4.50m, payee: "Shell", categoryId: coffee);
        await book.AddRuleAsync("Shell is fuel", "SHELL", fuel);

        ImportPreview preview = await book.PrepareAsync(
            account, OfxStatement(OfxRow("A1", "20260302", "-40.00", "SHELL OIL")));

        preview.Candidates[0].Suggestion.Source.ShouldBe(SuggestionSource.Rule);
        preview.Candidates[0].SuggestedCategoryId.ShouldBe(fuel);
        preview.RuleMatches.ShouldBe(1);
    }

    [Fact]
    public async Task A_rule_that_fires_is_counted_on_the_rules_screen()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int fuel = await book.CategoryIdAsync("Transport : Fuel");
        int ruleId = await book.AddRuleAsync("Shell is fuel", "SHELL", fuel);

        await book.ImportAsync(account, OfxStatement(
            OfxRow("A1", "20260302", "-40.00", "SHELL OIL"),
            OfxRow("A2", "20260305", "-35.00", "SHELL OIL")));

        RuleListItem rule = (await book.Rules.GetAllAsync()).Single(r => r.Id == ruleId);

        rule.TimesApplied.ShouldBe(2);
        rule.Rule.LastAppliedUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_rule_restricted_to_another_account_does_not_fire()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking");
        int card = await book.AddAccountAsync("Amex", AccountType.CreditCard);
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        await book.AddRuleAsync("Card fuel", "SHELL", fuel, accountId: card);

        ImportPreview preview = await book.PrepareAsync(
            checking, OfxStatement(OfxRow("A1", "20260302", "-40.00", "SHELL OIL")));

        preview.Candidates[0].Suggestion.Source.ShouldNotBe(SuggestionSource.Rule);
    }

    [Fact]
    public async Task With_enough_history_an_unknown_payee_gets_a_statistical_suggestion()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int coffee = await book.CategoryIdAsync("Food : Coffee");
        int fuel = await book.CategoryIdAsync("Transport : Fuel");

        // Enough of the user's own filing for the model to have learned something.
        for (int i = 0; i < 12; i++)
        {
            await book.AddTransactionAsync(
                account, -4.50m, new DateOnly(2026, 1, 1).AddDays(i),
                payee: $"Blue Bottle Coffee {i}", categoryId: coffee);
        }

        for (int i = 0; i < 12; i++)
        {
            await book.AddTransactionAsync(
                account, -40m, new DateOnly(2026, 2, 1).AddDays(i),
                payee: $"Shell Oil {i}", categoryId: fuel);
        }

        ImportPreview preview = await book.PrepareAsync(
            account, OfxStatement(OfxRow("A1", "20260302", "-5.25", "BLUE BOTTLE COFFEE 99")));

        ImportCandidate candidate = preview.Candidates[0];

        candidate.Suggestion.Source.ShouldBe(SuggestionSource.Statistical);
        candidate.SuggestedCategoryId.ShouldBe(coffee);
        candidate.IsGuess.ShouldBeTrue();
        candidate.Suggestion.Confidence.ShouldBeLessThan(1);
        candidate.SuggestionSourceText.ShouldContain("%");
    }

    [Fact]
    public async Task A_new_book_gets_no_statistical_guesses()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportPreview preview = await book.PrepareAsync(
            account, OfxStatement(OfxRow("A1", "20260302", "-5.25", "SOME MERCHANT")));

        // With nothing to learn from, guessing would be worse than saying nothing.
        preview.Candidates[0].Suggestion.Source.ShouldBe(SuggestionSource.None);
        preview.Guesses.ShouldBe(0);
    }

    [Fact]
    public async Task Payee_memory_still_beats_the_statistical_model()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int coffee = await book.CategoryIdAsync("Food : Coffee");
        int restaurants = await book.CategoryIdAsync("Food : Restaurants");

        for (int i = 0; i < 12; i++)
        {
            await book.AddTransactionAsync(
                account, -4.50m, new DateOnly(2026, 1, 1).AddDays(i),
                payee: $"Blue Bottle Coffee {i}", categoryId: coffee);
        }

        for (int i = 0; i < 12; i++)
        {
            await book.AddTransactionAsync(
                account, -40m, new DateOnly(2026, 2, 1).AddDays(i),
                payee: $"Shell Oil {i}", categoryId: coffee);
        }

        // This exact payee was last filed under restaurants, which is stronger evidence than
        // what the model infers from words it shares with other merchants.
        await book.AddTransactionAsync(
            account, -20m, new DateOnly(2026, 2, 20), payee: "Blue Bottle", categoryId: restaurants);

        ImportPreview preview = await book.PrepareAsync(
            account, OfxStatement(OfxRow("A1", "20260302", "-5.25", "BLUE BOTTLE")));

        preview.Candidates[0].Suggestion.Source.ShouldBe(SuggestionSource.PayeeMemory);
        preview.Candidates[0].SuggestedCategoryId.ShouldBe(restaurants);
    }

    [Fact]
    public async Task Transfers_and_voided_rows_are_kept_out_of_the_training_data()
    {
        using var book = new BookHarness();
        int checking = await book.AddAccountAsync("Checking", openingBalance: 5_000m);
        int savings = await book.AddAccountAsync("Savings", AccountType.Savings);

        // Neither of these represents a spending decision, so neither should teach the model
        // anything about where a category belongs.
        for (int i = 0; i < 30; i++)
        {
            await book.Register.SaveAsync(new TransactionDraft
            {
                AccountId = checking,
                Date = new DateOnly(2026, 1, 1).AddDays(i),
                Amount = Money.FromDecimal(-10m),
                PayeeName = "Recurring Transfer",
                TransferAccountId = savings,
            });
        }

        ImportPreview preview = await book.PrepareAsync(
            checking, OfxStatement(OfxRow("A1", "20260601", "-10.00", "RECURRING TRANSFER")));

        preview.Candidates[0].Suggestion.Source.ShouldBe(SuggestionSource.None);
    }

    private static string OfxRow(string fitId, string posted, string amount, string name) => $"""
        <STMTTRN>
        <TRNTYPE>DEBIT
        <DTPOSTED>{posted}
        <TRNAMT>{amount}
        <FITID>{fitId}
        <NAME>{name}
        </STMTTRN>
        """;

    private static string OfxStatement(params string[] rows) => $"""
        OFXHEADER:100
        DATA:OFXSGML

        <OFX>
        <BANKMSGSRSV1>
        <STMTTRNRS>
        <STMTRS>
        <CURDEF>USD
        <BANKACCTFROM>
        <BANKID>043000096
        <ACCTID>1234567890
        <ACCTTYPE>CHECKING
        </BANKACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20260101000000
        <DTEND>20261231000000
        {string.Join("\n", rows)}
        </BANKTRANLIST>
        </STMTRS>
        </STMTTRNRS>
        </BANKMSGSRSV1>
        </OFX>
        """;
}
