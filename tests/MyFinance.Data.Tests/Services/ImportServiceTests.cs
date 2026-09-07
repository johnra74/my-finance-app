using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;
using MyFinance.Import.Model;

namespace MyFinance.Data.Tests.Services;

public sealed class ImportServiceTests
{
    [Fact]
    public async Task A_statement_loads_into_the_register()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Everyday Checking", openingBalance: 1_000m);

        ImportSummary summary = await book.ImportAsync(account, Statement(
            Row("A1", "20260302", "-18.40", "BLUE BOTTLE"),
            Row("A2", "20260305", "-250.00", "LANDLORD"),
            Row("A3", "20260313", "3200.00", "ACME PAYROLL", type: "DIRECTDEP")));

        summary.Added.ShouldBe(3);

        RegisterView view = await book.Register.GetRegisterAsync(account);
        view.Lines.Count.ShouldBe(3);
        view.CurrentBalance.ShouldBe(Money.FromDecimal(3_931.60m));

        // A downloaded item has reached the bank by definition, so it arrives cleared and the
        // reconcile screen finds it already ticked.
        view.Lines.ShouldAllBe(l => l.Transaction.ClearedStatus == ClearedStatus.Cleared);
        view.ClearedBalance.ShouldBe(view.CurrentBalance);
    }

    [Fact]
    public async Task Every_imported_row_carries_the_banks_reference_and_its_batch()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportSummary summary = await book.ImportAsync(
            account, Statement(Row("A1", "20260302", "-18.40", "BLUE BOTTLE")));

        await using MyFinanceDbContext db = book.CreateContext();
        Transaction saved = await db.Transactions.SingleAsync();

        saved.FitId.ShouldBe("A1");
        saved.ImportBatchId.ShouldBe(summary.BatchId);
        saved.Splits.Count.ShouldBe(0); // not loaded on this query
    }

    [Fact]
    public async Task The_raw_bank_descriptor_is_kept_in_the_memo()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.ImportAsync(account, Statement(
            Row("A1", "20260302", "-18.40", "SQ *BLUE BOTTLE 1234", memo: "NEW YORK NY")));

        Transaction saved = (await book.Register.GetRegisterAsync(account)).Lines[0].Transaction;

        // The tidied name becomes the payee; nothing the bank wrote is thrown away.
        saved.Payee!.Name.ShouldBe("Blue Bottle");
        saved.Memo.ShouldBe("SQ *BLUE BOTTLE 1234 NEW YORK NY");
    }

    [Fact]
    public async Task Re_importing_the_same_file_adds_nothing()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        string file = Statement(
            Row("A1", "20260302", "-18.40", "BLUE BOTTLE"),
            Row("A2", "20260305", "-250.00", "LANDLORD"));

        await book.ImportAsync(account, file);

        // Every row is a duplicate, so nothing is selected and the attempt is refused rather
        // than quietly writing a second copy of the statement.
        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.ImportAsync(account, file));

        thrown.Errors.ShouldContain(e => e.Code == ImportService.NothingSelected);

        (await book.Register.GetRegisterAsync(account)).Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_overlapping_statement_adds_only_its_new_rows()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        await book.ImportAsync(account, Statement(
            Row("A1", "20260302", "-18.40", "BLUE BOTTLE"),
            Row("A2", "20260305", "-250.00", "LANDLORD")));

        // The next download repeats the tail of the last one, which is the normal case.
        ImportSummary second = await book.ImportAsync(account, Statement(
            Row("A2", "20260305", "-250.00", "LANDLORD"),
            Row("A3", "20260313", "3200.00", "ACME PAYROLL", type: "DIRECTDEP")));

        second.Added.ShouldBe(1);

        RegisterView view = await book.Register.GetRegisterAsync(account);
        view.Lines.Count.ShouldBe(3);
        view.CurrentBalance.ShouldBe(Money.FromDecimal(3_931.60m));
    }

    [Fact]
    public async Task The_resulting_balance_matches_what_the_statement_says()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        ImportSummary summary = await book.ImportAsync(account, Statement(
            [Row("A1", "20260302", "-18.40", "BLUE BOTTLE")],
            ledgerBalance: "981.60"));

        summary.BalanceAfter.ShouldBe(Money.FromDecimal(981.60m));
        summary.LedgerBalance.ShouldBe(Money.FromDecimal(981.60m));
        summary.AgreesWithStatement.ShouldBeTrue();
    }

    [Fact]
    public async Task Rows_on_one_date_get_distinct_increasing_sequences()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.AddTransactionAsync(account, -5m, new DateOnly(2026, 3, 2));

        await book.ImportAsync(account, Statement(
            Row("A1", "20260302", "-10.00", "ONE"),
            Row("A2", "20260302", "-20.00", "TWO"),
            Row("A3", "20260302", "-30.00", "THREE")));

        await using MyFinanceDbContext db = book.CreateContext();
        List<int> sequences = await db.Transactions
            .Where(t => t.FitId != null)
            .OrderBy(t => t.SequenceInDay)
            .Select(t => t.SequenceInDay)
            .ToListAsync();

        // Computed in one pass rather than a query per row, so this is exactly where an
        // off-by-one would show up as two rows sharing a position.
        sequences.Distinct().Count().ShouldBe(3);
        sequences.ShouldAllBe(s => s > 1);
    }

    [Fact]
    public async Task A_known_payee_pre_fills_its_remembered_category()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");
        int coffee = await book.CategoryIdAsync("Food : Coffee");

        await book.AddTransactionAsync(account, -4.50m, payee: "Blue Bottle", categoryId: coffee);

        ImportPreview preview = await book.PrepareAsync(
            account, Statement(Row("A1", "20260302", "-18.40", "BLUE BOTTLE")));

        preview.Candidates[0].SuggestedCategoryId.ShouldBe(coffee);
        preview.Candidates[0].SuggestedCategoryName.ShouldBe("Food : Coffee");

        await book.CommitAsync(account, preview);

        Transaction imported = (await book.Register.GetRegisterAsync(account))
            .Lines.Last().Transaction;

        imported.Splits.Single().CategoryId.ShouldBe(coffee);
    }

    [Fact]
    public async Task An_unrecognised_payee_lands_uncategorized_for_the_worklist()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        await book.ImportAsync(account, Statement(Row("A1", "20260302", "-18.40", "NEW MERCHANT")));

        (await book.Register.GetUncategorizedAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task One_payee_is_created_for_a_merchant_appearing_many_times()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportSummary summary = await book.ImportAsync(account, Statement(
            Row("A1", "20260302", "-4.50", "SQ *BLUE BOTTLE 1234"),
            Row("A2", "20260303", "-4.50", "SQ *BLUE BOTTLE 5678"),
            Row("A3", "20260304", "-4.50", "SQ *BLUE BOTTLE 9012")));

        // Three different store numbers, one merchant. Creating a payee per row is exactly
        // the mess this cleaning exists to prevent.
        summary.PayeesCreated.ShouldBe(1);

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Payees.CountAsync()).ShouldBe(1);
        (await db.Payees.SingleAsync()).Name.ShouldBe("Blue Bottle");
    }

    [Fact]
    public async Task A_correction_is_remembered_and_honoured_by_the_next_import()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportPreview first = await book.PrepareAsync(
            account, Statement(Row("A1", "20260302", "-4.50", "SQ *BLUE BOTTLE 1234")));

        // The user renames the suggested payee before committing.
        await book.CommitAsync(account, first, decisions:
        [
            new ImportRowDecision { Index = 0, Include = true, PayeeName = "Blue Bottle Coffee Co" },
        ]);

        // A later download of the same merchant, with a different store number.
        ImportPreview second = await book.PrepareAsync(
            account, Statement(Row("A2", "20260402", "-5.25", "SQ *BLUE BOTTLE 9999")));

        // The alias is keyed on the stable part of the descriptor, so the correction sticks
        // even though the raw text differs.
        second.Candidates[0].Payee.Name.ShouldBe("Blue Bottle Coffee Co");
        second.Candidates[0].Payee.IsNew.ShouldBeFalse();

        await book.CommitAsync(account, second);

        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Payees.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Reversing_the_signs_flips_the_whole_statement()
    {
        using var book = new BookHarness();
        int card = await book.AddAccountAsync("Rewards Card", AccountType.CreditCard);

        ImportPreview preview = await book.PrepareAsync(card, Statement(
            Row("A1", "20260302", "100.00", "PURCHASE"),
            Row("A2", "20260305", "50.00", "PURCHASE")));

        await book.CommitAsync(card, preview, reverseSigns: true);

        RegisterView view = await book.Register.GetRegisterAsync(card);
        view.CurrentBalance.ShouldBe(Money.FromDecimal(-150m));
    }

    [Fact]
    public async Task A_reversed_card_statement_is_detected_from_its_ledger_balance()
    {
        using var book = new BookHarness();
        int card = await book.AddAccountAsync("Rewards Card", AccountType.CreditCard);

        // The issuer reports purchases positive; the stated balance shows what is owed.
        ImportPreview preview = await book.PrepareAsync(card, CardStatement(
            [Row("A1", "20260302", "100.00", "PURCHASE")],
            ledgerBalance: "-100.00"));

        preview.Sign.Suggested.ShouldBe(SignPolarity.Inverted);
        preview.Sign.Confidence.ShouldBe(SignConfidence.Certain);
        preview.Sign.PreselectInversion.ShouldBeTrue();
    }

    [Fact]
    public async Task A_correctly_signed_statement_is_left_alone()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        ImportPreview preview = await book.PrepareAsync(account, Statement(
            [Row("A1", "20260302", "-18.40", "BLUE BOTTLE")],
            ledgerBalance: "981.60"));

        preview.Sign.Suggested.ShouldBe(SignPolarity.AsIs);
        preview.Sign.PreselectInversion.ShouldBeFalse();
        preview.Sign.AgreesWithLedger.ShouldBeTrue();
    }

    [Fact]
    public async Task Excluded_rows_are_not_written()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportPreview preview = await book.PrepareAsync(account, Statement(
            Row("A1", "20260302", "-10.00", "ONE"),
            Row("A2", "20260303", "-20.00", "TWO")));

        ImportSummary summary = await book.CommitAsync(account, preview, decisions:
        [
            new ImportRowDecision { Index = 0, Include = true },
            new ImportRowDecision { Index = 1, Include = false },
        ]);

        summary.Added.ShouldBe(1);
        (await book.Register.GetRegisterAsync(account)).Lines.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Selecting_nothing_is_refused_rather_than_writing_an_empty_batch()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportPreview preview = await book.PrepareAsync(
            account, Statement(Row("A1", "20260302", "-10.00", "ONE")));

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.CommitAsync(account, preview, decisions:
            [
                new ImportRowDecision { Index = 0, Include = false },
            ]));

        thrown.Errors.ShouldContain(e => e.Code == ImportService.NothingSelected);
    }

    [Fact]
    public async Task Importing_into_a_balance_only_account_is_refused()
    {
        using var book = new BookHarness();
        int mortgage = await book.AddAccountAsync("Woodgrove Mortgage", AccountType.UnsupportedImported);

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.PrepareAsync(mortgage, Statement(Row("A1", "20260302", "-10.00", "ONE"))));

        thrown.Errors.ShouldContain(e => e.Code == ImportService.AccountReadOnly);
    }

    [Fact]
    public async Task Undoing_an_import_removes_exactly_its_rows_and_restores_the_balance()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        await book.AddTransactionAsync(account, -25m, new DateOnly(2026, 2, 1));
        Money before = (await book.Register.GetRegisterAsync(account)).CurrentBalance;

        ImportSummary summary = await book.ImportAsync(account, Statement(
            Row("A1", "20260302", "-18.40", "BLUE BOTTLE"),
            Row("A2", "20260305", "-250.00", "LANDLORD")));

        int removed = await book.Import.RevertAsync(summary.BatchId);

        removed.ShouldBe(2);

        RegisterView view = await book.Register.GetRegisterAsync(account);
        view.Lines.Count.ShouldBe(1);
        view.CurrentBalance.ShouldBe(before);
    }

    [Fact]
    public async Task Undoing_keeps_the_payees_and_the_mappings_it_learned()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportSummary summary = await book.ImportAsync(
            account, Statement(Row("A1", "20260302", "-4.50", "SQ *BLUE BOTTLE 1234")));

        await book.Import.RevertAsync(summary.BatchId);

        // The payee may be referenced elsewhere by now, and the descriptor mapping is the
        // most valuable thing the import produced.
        await using MyFinanceDbContext db = book.CreateContext();
        (await db.Payees.CountAsync()).ShouldBe(1);
        (await db.PayeeAliases.CountAsync()).ShouldBe(1);
        (await db.Transactions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Undoing_the_same_import_twice_is_refused()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportSummary summary = await book.ImportAsync(
            account, Statement(Row("A1", "20260302", "-10.00", "ONE")));

        await book.Import.RevertAsync(summary.BatchId);

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Import.RevertAsync(summary.BatchId));

        thrown.Errors.ShouldContain(e => e.Code == ImportService.AlreadyReverted);
    }

    [Fact]
    public async Task Undoing_is_refused_once_a_row_has_been_reconciled()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking", openingBalance: 1_000m);

        ImportSummary summary = await book.ImportAsync(
            account, Statement(Row("A1", "20260302", "-18.40", "BLUE BOTTLE")));

        int transactionId = (await book.Register.GetRegisterAsync(account)).Lines[0].Transaction.Id;
        await book.Reconcile.CompleteAsync(
            account, new DateOnly(2026, 3, 31), Money.FromDecimal(981.60m), [transactionId]);

        var thrown = await Should.ThrowAsync<BookValidationException>(
            () => book.Import.RevertAsync(summary.BatchId));

        // Removing a reconciled row would invalidate a balance already agreed with the bank.
        thrown.Errors.ShouldContain(e => e.Code == ImportService.BatchReconciled);
        (await book.Register.GetRegisterAsync(account)).Lines.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_import_history_records_what_happened()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Checking");

        ImportSummary summary = await book.ImportAsync(
            account,
            Statement(Row("A1", "20260302", "-10.00", "ONE"), Row("A2", "20260303", "-20.00", "TWO")),
            fileName: "pnc-march.ofx");

        IReadOnlyList<ImportHistoryEntry> history = await book.Import.GetHistoryAsync();

        ImportHistoryEntry entry = history.Single();
        entry.Id.ShouldBe(summary.BatchId);
        entry.AccountName.ShouldBe("Checking");
        entry.Batch.SourceFileName.ShouldBe("pnc-march.ofx");
        entry.Batch.TransactionsAdded.ShouldBe(2);
        entry.Batch.PeriodStart.ShouldBe(new DateOnly(2026, 3, 2));
        entry.Batch.PeriodEnd.ShouldBe(new DateOnly(2026, 3, 3));
        entry.RemainingTransactions.ShouldBe(2);
        entry.CanRevert.ShouldBeTrue();

        await book.Import.RevertAsync(summary.BatchId);

        ImportHistoryEntry after = (await book.Import.GetHistoryAsync()).Single();
        after.IsReverted.ShouldBeTrue();
        after.CanRevert.ShouldBeFalse();

        // The counters are kept, so the history reads "2 added, later undone".
        after.Batch.TransactionsAdded.ShouldBe(2);
    }

    [Fact]
    public async Task An_account_is_recognised_by_the_next_statement_from_the_same_bank()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Everyday Checking");

        ImportedStatement statement = book.ParseStatement(
            Statement(Row("A1", "20260302", "-10.00", "ONE")));

        // Nothing to match on yet.
        (await book.Import.MatchAccountAsync(statement.Account)).ShouldBeNull();

        await book.Import.LinkAccountAsync(account, statement.Account);

        (await book.Import.MatchAccountAsync(statement.Account)).ShouldBe(account);
    }

    [Fact]
    public async Task Linking_stores_a_digest_and_never_the_account_number()
    {
        using var book = new BookHarness();
        int account = await book.AddAccountAsync("Everyday Checking");

        ImportedStatement statement = book.ParseStatement(
            Statement([Row("A1", "20260302", "-10.00", "ONE")], accountId: "1234567890123456"));

        await book.Import.LinkAccountAsync(account, statement.Account);

        Account saved = (await book.Accounts.FindAsync(account))!;

        saved.OfxAccountKey.ShouldNotBeNullOrWhiteSpace();
        saved.OfxAccountKey!.Length.ShouldBe(64);
        saved.OfxAccountKey.ShouldNotContain("1234567890123456");

        // Only the masked form is ever displayed or kept.
        saved.AccountNumberMasked.ShouldBe("XXXXXXXXXXXX3456");
    }

    private static string Row(
        string fitId,
        string posted,
        string amount,
        string name,
        string? memo = null,
        string type = "DEBIT")
    {
        string memoLine = memo is null ? string.Empty : $"\n<MEMO>{memo}";

        return $"""
            <STMTTRN>
            <TRNTYPE>{type}
            <DTPOSTED>{posted}
            <TRNAMT>{amount}
            <FITID>{fitId}
            <NAME>{name}{memoLine}
            </STMTTRN>
            """;
    }

    private static string Statement(params string[] rows) => Statement(rows, "0.00");

    private static string Statement(
        string[] rows,
        string ledgerBalance = "0.00",
        string accountId = "1234567890") => $"""
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102

        <OFX>
        <BANKMSGSRSV1>
        <STMTTRNRS>
        <STMTRS>
        <CURDEF>USD
        <BANKACCTFROM>
        <BANKID>043000096
        <ACCTID>{accountId}
        <ACCTTYPE>CHECKING
        </BANKACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20260101000000
        <DTEND>20261231000000
        {string.Join("\n", rows)}
        </BANKTRANLIST>
        <LEDGERBAL>
        <BALAMT>{ledgerBalance}
        <DTASOF>20261231000000
        </LEDGERBAL>
        </STMTRS>
        </STMTTRNRS>
        </BANKMSGSRSV1>
        </OFX>
        """;

    private static string CardStatement(string[] rows, string ledgerBalance) => $"""
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102

        <OFX>
        <CREDITCARDMSGSRSV1>
        <CCSTMTTRNRS>
        <CCSTMTRS>
        <CURDEF>USD
        <CCACCTFROM>
        <ACCTID>374245001234567
        </CCACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20260101000000
        <DTEND>20261231000000
        {string.Join("\n", rows)}
        </BANKTRANLIST>
        <LEDGERBAL>
        <BALAMT>{ledgerBalance}
        <DTASOF>20261231000000
        </LEDGERBAL>
        </CCSTMTRS>
        </CCSTMTTRNRS>
        </CREDITCARDMSGSRSV1>
        </OFX>
        """;
}
