using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Export;
using MyFinance.Core.Primitives;
using MyFinance.Core.Progress;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// Exporting the whole book to a file that can be read without this application.
/// </summary>
/// <remarks>
/// The property that matters here is arithmetic, not shape: an export that parses but has
/// quietly dropped a row looks complete and is worse than one that fails.
/// </remarks>
public class BookExportServiceTests
{
    [Fact]
    public async Task Every_account_category_payee_and_transaction_is_exported()
    {
        using var harness = new BookHarness();

        int everyday = await harness.AddAccountAsync("Everyday", openingBalance: 250m);
        int savings = await harness.AddAccountAsync("Savings", AccountType.Savings);
        int groceries = await harness.CategoryIdAsync("Food : Groceries");

        await harness.AddTransactionAsync(everyday, -42.50m, payee: "Blue Bottle", categoryId: groceries);
        await harness.AddTransactionAsync(savings, 100m, payee: "Interest");

        BookDocument document = await harness.Export.BuildAsync();

        document.Accounts.Select(a => a.Name).ShouldBe(["Everyday", "Savings"], ignoreOrder: true);
        document.Categories.ShouldNotBeEmpty();
        document.Payees.Select(p => p.Name).ShouldContain("Blue Bottle");
        document.Transactions.Count.ShouldBe(2);

        ExportAccount exported = document.Accounts.Single(a => a.Name == "Everyday");
        exported.OpeningBalance.ShouldBe("250.00");
        exported.OpeningBalanceMinorUnits.ShouldBe(25000);
        exported.Type.ShouldBe("Checking");
        exported.Group.ShouldBe("Bank");
    }

    [Fact]
    public async Task Splits_are_nested_inside_their_transaction()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday");
        int groceries = await harness.CategoryIdAsync("Food : Groceries");
        int fuel = await harness.CategoryIdAsync("Transport : Fuel");

        await harness.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            Date = new DateOnly(2026, 3, 1),
            Amount = Money.FromDecimal(-100m),
            PayeeName = "Supermarket",
            Splits =
            [
                new SplitDraft { CategoryId = groceries, Amount = Money.FromDecimal(-60m) },
                new SplitDraft { CategoryId = fuel, Amount = Money.FromDecimal(-40m) },
            ],
        });

        BookDocument document = await harness.Export.BuildAsync();
        ExportTransaction transaction = document.Transactions.Single();

        transaction.Splits.Count.ShouldBe(2);
        transaction.Splits.Sum(s => s.AmountMinorUnits).ShouldBe(transaction.AmountMinorUnits);
    }

    [Fact]
    public async Task Each_transfer_leg_names_the_other()
    {
        using var harness = new BookHarness();

        int from = await harness.AddAccountAsync("Everyday", openingBalance: 500m);
        int to = await harness.AddAccountAsync("Savings", AccountType.Savings);

        await harness.Register.SaveAsync(new TransactionDraft
        {
            AccountId = from,
            Date = new DateOnly(2026, 3, 1),
            Amount = Money.FromDecimal(-200m),
            TransferAccountId = to,
        });

        BookDocument document = await harness.Export.BuildAsync();

        // Named rather than inferred. This is the field that ruled out QIF as a format: a
        // reader must not have to guess which rows pair up from their amounts and dates.
        ExportTransaction[] legs = [.. document.Transactions];
        legs.Length.ShouldBe(2);

        foreach (ExportTransaction leg in legs)
        {
            leg.TransferPeerId.ShouldNotBeNull();
            legs.ShouldContain(other => other.Id == leg.TransferPeerId);
        }

        legs.Sum(l => l.AmountMinorUnits).ShouldBe(0);
    }

    // -- The things it is easy to forget ------------------------------------------------

    [Fact]
    public async Task An_archived_category_is_exported()
    {
        using var harness = new BookHarness();

        int groceries = await harness.CategoryIdAsync("Food : Groceries");
        await harness.Categories.SetArchivedAsync(groceries, true);

        BookDocument document = await harness.Export.BuildAsync();

        document.Categories.Single(c => c.Id == groceries).IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task A_closed_account_and_its_history_are_exported()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Old Card", AccountType.CreditCard);
        await harness.AddTransactionAsync(account, -30m, payee: "Shop");
        await harness.Accounts.SetClosedAsync(account, true);

        BookDocument document = await harness.Export.BuildAsync();

        document.Accounts.Single(a => a.Id == account).IsClosed.ShouldBeTrue();
        document.Transactions.Count(t => t.AccountId == account).ShouldBe(1);
    }

    [Fact]
    public async Task A_voided_transaction_is_exported_with_its_flag()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday");
        int id = await harness.AddTransactionAsync(account, -30m, payee: "Shop");
        await harness.Register.SetVoidAsync(id, true);

        BookDocument document = await harness.Export.BuildAsync();

        document.Transactions.Single(t => t.Id == id).IsVoid.ShouldBeTrue();
    }

    [Fact]
    public async Task Payee_memory_and_aliases_are_exported()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday");
        int groceries = await harness.CategoryIdAsync("Food : Groceries");
        await harness.AddTransactionAsync(account, -20m, payee: "Blue Bottle", categoryId: groceries);

        BookDocument document = await harness.Export.BuildAsync();
        ExportPayee payee = document.Payees.Single(p => p.Name == "Blue Bottle");

        // Decades of decisions about which shop belongs in which category — the thing that
        // makes a restored book immediately useful rather than merely complete.
        payee.LastCategoryId.ShouldBe(groceries);
        payee.NormalizedName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_scheduled_series_is_exported_with_what_was_entered_or_skipped()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday", openingBalance: 1000m);
        int bill = await harness.AddBillAsync(account, "Electricity", -80m, new DateOnly(2026, 1, 5));

        await harness.Schedules.EnterAllDueAsync(bill, new DateOnly(2026, 3, 31));

        BookDocument document = await harness.Export.BuildAsync();
        ExportScheduled series = document.Scheduled.Single();

        series.Frequency.ShouldBe("Monthly");
        series.StartDate.ShouldBe("2026-01-05");

        // Without the occurrence history a reconstructed book would not know which bills had
        // already been paid, and would enter every backdated one a second time.
        series.Occurrences.ShouldNotBeEmpty();
        series.Occurrences.ShouldContain(o => o.State == "Entered");
    }

    [Fact]
    public async Task Budgets_and_watched_categories_are_exported()
    {
        using var harness = new BookHarness();

        int groceries = await harness.CategoryIdAsync("Food : Groceries");
        await harness.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries,
            PeriodStart = new DateOnly(2026, 3, 1),
            Amount = Money.FromDecimal(400m),
        });
        await harness.Budgets.ToggleWatchAsync(groceries);

        BookDocument document = await harness.Export.BuildAsync();

        ExportBudgetLine line = document.Budgets.Lines.Single();
        line.CategoryId.ShouldBe(groceries);
        line.Amount.ShouldBe("400.00");
        line.PeriodStart.ShouldBe("2026-03-01");

        document.Budgets.WatchedCategoryIds.ShouldBe([groceries]);
    }

    [Fact]
    public async Task Rules_are_exported_in_priority_order()
    {
        using var harness = new BookHarness();

        int fuel = await harness.CategoryIdAsync("Transport : Fuel");
        int groceries = await harness.CategoryIdAsync("Food : Groceries");

        int first = await harness.AddRuleAsync("Shell", "SHELL", fuel);
        int second = await harness.AddRuleAsync("Supermarket", "TESCO", groceries);

        BookDocument document = await harness.Export.BuildAsync();

        // For a rule the order *is* the meaning — the first match wins — so a document that
        // lists them in id order would be describing different behaviour.
        document.Rules.Select(r => r.Id).ShouldBe([first, second]);
        document.Rules.Select(r => r.Priority).ShouldBeInOrder();
    }

    [Fact]
    public async Task Every_import_batch_id_on_a_transaction_resolves_to_an_exported_batch()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday");

        await harness.ImportQifAsync(account, """
            !Type:Bank
            D03/01/2026
            T-25.00
            PBlue Bottle
            ^
            """);

        BookDocument document = await harness.Export.BuildAsync();

        document.ImportBatches.ShouldNotBeEmpty();

        // A published format may not carry a dangling reference. Import batches were nearly
        // omitted as internal undo machinery, which would have left every imported row
        // pointing at nothing.
        foreach (ExportTransaction transaction in document.Transactions)
        {
            if (transaction.ImportBatchId is int batchId)
            {
                document.ImportBatches.ShouldContain(b => b.Id == batchId);
            }
        }
    }

    [Fact]
    public async Task Every_id_referenced_in_the_document_resolves_to_something_in_it()
    {
        using var harness = new BookHarness();

        int everyday = await harness.AddAccountAsync("Everyday", openingBalance: 500m);
        int savings = await harness.AddAccountAsync("Savings", AccountType.Savings);
        int groceries = await harness.CategoryIdAsync("Food : Groceries");

        await harness.AddTransactionAsync(everyday, -42.50m, payee: "Blue Bottle", categoryId: groceries);
        await harness.Register.SaveAsync(new TransactionDraft
        {
            AccountId = everyday,
            Date = new DateOnly(2026, 3, 2),
            Amount = Money.FromDecimal(-200m),
            TransferAccountId = savings,
        });
        await harness.AddBillAsync(everyday, "Electricity", -80m, new DateOnly(2026, 1, 5));
        await harness.Budgets.SetAsync(new BudgetDraft
        {
            CategoryId = groceries,
            PeriodStart = new DateOnly(2026, 3, 1),
            Amount = Money.FromDecimal(400m),
        });
        await harness.AddRuleAsync("Shell", "SHELL", groceries);

        BookDocument document = await harness.Export.BuildAsync();

        var accounts = document.Accounts.Select(a => a.Id).ToHashSet();
        var categories = document.Categories.Select(c => c.Id).ToHashSet();
        var payees = document.Payees.Select(p => p.Id).ToHashSet();
        var transactions = document.Transactions.Select(t => t.Id).ToHashSet();
        var batches = document.ImportBatches.Select(b => b.Id).ToHashSet();
        var scheduled = document.Scheduled.Select(s => s.Id).ToHashSet();

        foreach (ExportTransaction t in document.Transactions)
        {
            accounts.ShouldContain(t.AccountId);
            if (t.PayeeId is int p) payees.ShouldContain(p);
            if (t.TransferPeerId is int peer) transactions.ShouldContain(peer);
            if (t.ImportBatchId is int b) batches.ShouldContain(b);
            if (t.ScheduledTransactionId is int s) scheduled.ShouldContain(s);
            foreach (ExportSplit split in t.Splits)
            {
                if (split.CategoryId is int c) categories.ShouldContain(c);
            }
        }

        foreach (ExportCategory c in document.Categories)
        {
            if (c.ParentId is int parent) categories.ShouldContain(parent);
        }

        foreach (ExportPayee p in document.Payees)
        {
            if (p.LastCategoryId is int c) categories.ShouldContain(c);
        }

        foreach (ExportScheduled s in document.Scheduled)
        {
            accounts.ShouldContain(s.AccountId);
            if (s.PayeeId is int p) payees.ShouldContain(p);
        }

        foreach (ExportBudgetLine b in document.Budgets.Lines) categories.ShouldContain(b.CategoryId);
        foreach (int w in document.Budgets.WatchedCategoryIds) categories.ShouldContain(w);
        foreach (ExportRule r in document.Rules)
        {
            if (r.AccountId is int a) accounts.ShouldContain(a);
            if (r.TargetCategoryId is int c) categories.ShouldContain(c);
            if (r.TargetPayeeId is int p) payees.ShouldContain(p);
        }
    }

    // -- Nothing secret, nothing derived ------------------------------------------------

    [Fact]
    public async Task No_account_key_digest_or_book_secret_appears_in_the_document()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday");

        await harness.ImportQifAsync(account, """
            !Type:Bank
            D03/01/2026
            T-25.00
            PBlue Bottle
            ^
            """);

        byte[] secret = await harness.Settings.GetOrCreateOfxSecretAsync();
        string secretText = Convert.ToBase64String(secret);

        string json = (await harness.Export.BuildAsync()).ToJson();

        // Asserted against the serialised text rather than the model: the point is that
        // nothing reaches the file, however it got into the object graph.
        json.ShouldNotContain(secretText);
        json.ShouldNotContain("ofxAccountKey", Case.Insensitive);
        json.ShouldNotContain("account_key_secret");
    }

    [Fact]
    public async Task No_payee_embedding_or_derived_balance_appears_in_the_document()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday", openingBalance: 250m);
        await harness.AddTransactionAsync(account, -42.50m, payee: "Blue Bottle");

        string json = (await harness.Export.BuildAsync()).ToJson();

        json.ShouldNotContain("embedding", Case.Insensitive);
        json.ShouldNotContain("runningBalance", Case.Insensitive);
        json.ShouldNotContain("currentBalance", Case.Insensitive);
    }

    // -- The property that matters ------------------------------------------------------

    [Fact]
    public async Task Every_exported_account_balance_agrees_with_the_book()
    {
        using var harness = new BookHarness();

        int everyday = await harness.AddAccountAsync("Everyday", openingBalance: 250m);
        int card = await harness.AddAccountAsync("Card", AccountType.CreditCard);
        int savings = await harness.AddAccountAsync("Savings", AccountType.Savings, openingBalance: 1000m);

        await harness.AddTransactionAsync(everyday, -42.50m, payee: "Blue Bottle");
        await harness.AddTransactionAsync(everyday, 1200m, payee: "Salary");
        await harness.AddTransactionAsync(card, -318.99m, payee: "Airline");

        int voided = await harness.AddTransactionAsync(everyday, -999m, payee: "Mistake");
        await harness.Register.SetVoidAsync(voided, true);

        await harness.Register.SaveAsync(new TransactionDraft
        {
            AccountId = everyday,
            Date = new DateOnly(2026, 3, 2),
            Amount = Money.FromDecimal(-200m),
            TransferAccountId = savings,
        });

        BookDocument document = await harness.Export.BuildAsync();

        // Totalled from the document alone, the way an outside reader would have to.
        foreach (ExportAccount account in document.Accounts)
        {
            long fromDocument = account.OpeningBalanceMinorUnits
                + document.Transactions
                    .Where(t => t.AccountId == account.Id && !t.IsVoid)
                    .Sum(t => t.AmountMinorUnits);

            MyFinance.Core.Accounts.AccountSummary inBook =
                (await harness.Accounts.GetAccountListAsync())
                    .AllAccounts.Single(a => a.Id == account.Id);

            fromDocument.ShouldBe(
                inBook.CurrentBalance.MinorUnits,
                $"'{account.Name}' totals differently in the export than in the book");
        }
    }

    [Fact]
    public async Task The_document_holds_exactly_as_many_rows_as_the_book()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday");
        int groceries = await harness.CategoryIdAsync("Food : Groceries");

        for (int i = 1; i <= 25; i++)
        {
            await harness.AddTransactionAsync(
                account, -i, new DateOnly(2026, 3, 1), payee: $"Shop {i}", categoryId: groceries);
        }

        BookDocument document = await harness.Export.BuildAsync();

        await using MyFinanceDbContext db = harness.CreateContext();

        // An export that silently omits something looks complete, which is worse than one
        // that fails.
        document.Accounts.Count.ShouldBe(await db.Accounts.CountAsync());
        document.Categories.Count.ShouldBe(await db.Categories.CountAsync());
        document.Payees.Count.ShouldBe(await db.Payees.CountAsync());
        document.Transactions.Count.ShouldBe(await db.Transactions.CountAsync());
        document.Transactions.Sum(t => t.Splits.Count).ShouldBe(await db.TransactionSplits.CountAsync());
    }

    // -- Running it ---------------------------------------------------------------------

    [Fact]
    public async Task An_export_reports_progress_and_can_be_stopped()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Everyday");
        await harness.AddTransactionAsync(account, -10m, payee: "Shop");

        var stages = new List<string>();
        var progress = new Progress<WorkProgress>(p => stages.Add(p.Stage));

        await harness.Export.BuildAsync(progress);

        await Task.Delay(50);
        stages.ShouldNotBeEmpty();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Export.BuildAsync(null, cancelled.Token));
    }

    [Fact]
    public async Task Stopping_an_export_leaves_no_half_written_file()
    {
        using var harness = new BookHarness();
        int account = await harness.AddAccountAsync("Everyday");
        await harness.AddTransactionAsync(account, -10m, payee: "Shop");

        string path = Path.Combine(Path.GetTempPath(), $"myfinance-export-{Guid.NewGuid():N}.json");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Export.ExportAsync(path, null, cancelled.Token));

        File.Exists(path).ShouldBeFalse();
        File.Exists(path + ".partial").ShouldBeFalse();
    }

    [Fact]
    public async Task An_exported_file_reads_back_as_the_book_it_came_from()
    {
        using var harness = new BookHarness();

        int account = await harness.AddAccountAsync("Everyday", openingBalance: 250m);
        await harness.AddTransactionAsync(account, -42.50m, payee: "Blue Bottle");

        string path = Path.Combine(Path.GetTempPath(), $"myfinance-export-{Guid.NewGuid():N}.json");

        try
        {
            await harness.Export.ExportAsync(path);

            File.Exists(path).ShouldBeTrue();
            File.Exists(path + ".partial").ShouldBeFalse();

            // Read the way somebody with no access to this application would have to.
            BookDocument reread = BookDocument.FromJson(await File.ReadAllTextAsync(path));

            reread.Accounts.Single().Name.ShouldBe("Everyday");
            reread.Transactions.Single().Amount.ShouldBe("-42.50");
            reread.Transactions.Single().Payee(reread).ShouldBe("Blue Bottle");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task The_documented_shape_matches_what_the_service_writes()
    {
        using var harness = new BookHarness();

        // Rich enough to exercise the optional keys too: a null is omitted from the document
        // rather than written, so a thin book would not prove the shape.
        int account = await harness.Accounts.CreateAsync(new AccountDraft
        {
            Name = "Everyday",
            Type = AccountType.Checking,
            AccountNumberMasked = "XXXXXXXX6464",
            OpeningBalance = Money.FromDecimal(250m),
        });

        int savings = await harness.AddAccountAsync("Savings", AccountType.Savings);
        int groceries = await harness.CategoryIdAsync("Food : Groceries");

        await harness.AddTransactionAsync(account, -42.50m, payee: "Blue Bottle", categoryId: groceries);

        await harness.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            Date = new DateOnly(2026, 3, 2),
            Amount = Money.FromDecimal(-100m),
            TransferAccountId = savings,
        });

        string json = (await harness.Export.BuildAsync()).ToJson();

        // Every key documented in specs/012-full-book-export/data-model.md. This is what stops
        // the documentation and the code drifting apart, which is the ordinary fate of a
        // published format nobody checks.
        string[] documented =
        [
            "\"format\"", "\"formatVersion\"", "\"exportedUtc\"", "\"application\"",
            "\"accounts\"", "\"categories\"", "\"payees\"", "\"transactions\"",
            "\"scheduled\"", "\"budgets\"", "\"rules\"", "\"merchantCodes\"",
            "\"importBatches\"",
            "\"accountNumberMasked\"", "\"openingBalance\"", "\"openingBalanceMinorUnits\"",
            "\"fullName\"", "\"isArchived\"", "\"normalizedName\"", "\"lastCategoryId\"",
            "\"aliases\"", "\"sequenceInDay\"", "\"transferPeerId\"", "\"splits\"",
            "\"clearedStatus\"", "\"isVoid\"",
        ];

        foreach (string key in documented)
        {
            json.Contains(key, StringComparison.Ordinal)
                .ShouldBeTrue($"the documented key {key} is missing from the export");
        }
    }

    [Fact]
    public async Task An_empty_book_exports_as_an_empty_book_rather_than_failing()
    {
        using var harness = new BookHarness();

        BookDocument document = await harness.Export.BuildAsync();

        document.Accounts.ShouldBeEmpty();
        document.Transactions.ShouldBeEmpty();
        document.Categories.ShouldNotBeEmpty("a new book is seeded with a chart of categories");
        document.ToJson().ShouldNotBeNullOrWhiteSpace();
    }
}

file static class ExportReadingExtensions
{
    /// <summary>Resolves a transaction's payee the way an outside reader would have to.</summary>
    public static string? Payee(this ExportTransaction transaction, BookDocument document) =>
        transaction.PayeeId is int id
            ? document.Payees.SingleOrDefault(p => p.Id == id)?.Name
            : null;
}
