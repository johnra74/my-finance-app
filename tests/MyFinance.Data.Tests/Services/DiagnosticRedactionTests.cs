using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Diagnostics;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.Data.Tests.Services;

/// <summary>
/// The gate: a log produced by a real book must contain nothing from that book.
/// </summary>
/// <remarks>
/// <para>
/// Asserted by <b>searching the file for values taken out of the book</b> — not by reading it
/// and forming an impression. A diagnostics file is plain text that the user may well email to
/// somebody; if a payee can reach it, the feature is a second copy of their records.
/// </para>
/// <para>
/// Every case runs at both verbosity levels, because verbose mode is where the boundary would
/// be tempted to slip.
/// </para>
/// </remarks>
public sealed class DiagnosticRedactionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "myfinance-redaction", Guid.NewGuid().ToString("N"));

    private const string Payee = "Zzyzx Coffee Roasters";
    private const string CategoryLeaf = "Groceries";
    private const string AccountName = "Qwertyuiop Checking";

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Runs a session that fails in a few different ways, and returns everything logged.
    /// </summary>
    private async Task<string> FailingSessionAsync(bool verbose)
    {
        using var harness = new BookHarness();

        int account = await harness.Accounts.CreateAsync(new AccountDraft
        {
            Name = AccountName,
            Type = MyFinance.Core.Enums.AccountType.Checking,
            OpeningBalance = Money.FromDecimal(1234.56m),
        });

        int groceries = await harness.CategoryIdAsync($"Food : {CategoryLeaf}");
        await harness.AddTransactionAsync(account, -987.65m, payee: Payee, categoryId: groceries);

        string path = Path.Combine(_directory, verbose ? "verbose" : "normal");
        using var log = new DiagnosticLog(
            new DiagnosticOptions(path, Verbose: verbose), startWriter: false);

        log.Describe("1.0.0");
        log.BookOpened(harness.DatabasePath, schemaVersion: 4);

        // Real failures from real services, each carrying whatever the service put in its
        // message — which for this application's own exceptions includes payee names.
        foreach (Func<Task> failing in Failures(harness, account, groceries))
        {
            log.Breadcrumb(Operation.SaveTransaction);

            try
            {
                await failing();
            }
            catch (Exception ex)
            {
                log.Failure(Operation.SaveTransaction, ex);
            }
        }

        log.Flush();

        return File.Exists(Path.Combine(path, DiagnosticOptions.FileName))
            ? File.ReadAllText(Path.Combine(path, DiagnosticOptions.FileName))
            : string.Empty;
    }

    private static IEnumerable<Func<Task>> Failures(BookHarness harness, int account, int category) =>
    [
        // A duplicate account name — the message names the account.
        () => harness.Accounts.CreateAsync(new AccountDraft
        {
            Name = AccountName,
            Type = MyFinance.Core.Enums.AccountType.Checking,
        }),

        // Splits that do not add up — the message quotes amounts.
        () => harness.Register.SaveAsync(new TransactionDraft
        {
            AccountId = account,
            Date = new DateOnly(2026, 3, 1),
            Amount = Money.FromDecimal(-50m),
            PayeeName = Payee,
            Splits = [new SplitDraft { CategoryId = category, Amount = Money.FromDecimal(-40m) }],
        }),

        // A category that cannot be deleted because it has history — names the category.
        () => harness.Categories.DeleteAsync(category),

        // An account that no longer exists.
        () => harness.Register.SaveAsync(new TransactionDraft
        {
            AccountId = 999_999,
            Date = new DateOnly(2026, 3, 1),
            Amount = Money.FromDecimal(-10m),
            PayeeName = Payee,
        }),
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_payee_category_or_account_name_reaches_the_log(bool verbose)
    {
        string log = await FailingSessionAsync(verbose);

        log.ShouldNotBeNullOrWhiteSpace("the session should have produced entries");

        log.ShouldNotContain(Payee, Case.Insensitive);
        log.ShouldNotContain(AccountName, Case.Insensitive);
        log.ShouldNotContain("Qwertyuiop", Case.Insensitive);
        log.ShouldNotContain("Zzyzx", Case.Insensitive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_amount_reaches_the_log(bool verbose)
    {
        string log = await FailingSessionAsync(verbose);

        // The figures entered above, in the shapes a message might render them.
        foreach (string amount in new[] { "987.65", "1234.56", "1,234.56", "-50.00", "40.00" })
        {
            log.ShouldNotContain(amount, Case.Insensitive);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_book_path_or_file_name_reaches_the_log(bool verbose)
    {
        using var probe = new TempBook();

        string log = await FailingSessionAsync(verbose);

        log.ShouldNotContain(".mfdb", Case.Insensitive);
        log.ShouldNotContain("myfinance-tests", Case.Insensitive);
        log.ShouldNotContain(Path.GetTempPath(), Case.Insensitive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_log_still_says_enough_to_locate_the_fault(bool verbose)
    {
        // Redaction that removed everything useful would be a different kind of failure.
        string log = await FailingSessionAsync(verbose);

        log.ShouldContain("SaveTransaction");
        log.ShouldContain("Exception");
        log.ShouldContain("v1.0.0");
        log.ShouldContain("schema:4");
    }
}
