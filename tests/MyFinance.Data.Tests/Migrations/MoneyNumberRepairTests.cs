using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Data.Migrations;
using MyFinance.Data.Security;
using MyFinance.Import.Mny;

using MyFinance.Data.Tests.Services;

namespace MyFinance.Data.Tests.Migrations;

/// <summary>
/// The one-off repair of cheque numbers already written into a book.
/// </summary>
/// <remarks>
/// <para>
/// The reader now decodes Money's sort flag, which fixes every future migration. This fixes
/// the books already on disk, and it runs the SQL the migration runs — not a paraphrase of it.
/// </para>
/// <para>
/// The rule is stated twice, once in C# and once in SQL, and that is the risk this carries.
/// <see cref="The_repair_and_the_reader_agree"/> is what makes it a manageable one.
/// </para>
/// </remarks>
public sealed class MoneyNumberRepairTests
{
    /// <summary>Encoded values and what each must become. Both statements of the rule see this list.</summary>
    private static readonly (string Stored, string Expected)[] Cases =
    [
        ("0        1168", "1168"),
        ("0         866", "866"),
        ("0       12345", "12345"),
        ("1ATM", "ATM"),
        ("1Deposit", "Deposit"),
        ("1ATM withdrawal", "ATM withdrawal"),

        // Hand-entered. A repair that touched any of these would corrupt a number nobody
        // could recover, in a file with no history.
        ("1234", "1234"),
        ("101", "101"),
        ("1", "1"),
        ("0", "0"),
        ("0123", "0123"),
        ("1A2", "1A2"),
        ("ATM", "ATM"),
        ("0            1", "0            1"),
    ];

    [Fact]
    public async Task The_repair_decodes_the_numbers_the_migration_left_behind()
    {
        using var harness = new BookHarness();

        IReadOnlyDictionary<string, string> after = await RepairAsync(harness).ConfigureAwait(true);

        after["0        1168"].ShouldBe("1168");
        after["1ATM"].ShouldBe("ATM");
        after["0         866"].ShouldBe("866");
    }

    [Fact]
    public async Task The_repair_leaves_a_hand_typed_number_alone()
    {
        using var harness = new BookHarness();

        IReadOnlyDictionary<string, string> after = await RepairAsync(harness).ConfigureAwait(true);

        after["1234"].ShouldBe("1234");
        after["101"].ShouldBe("101");
        after["0123"].ShouldBe("0123");
        after["1A2"].ShouldBe("1A2");
    }

    [Fact]
    public async Task The_repair_and_the_reader_agree()
    {
        using var harness = new BookHarness();

        IReadOnlyDictionary<string, string> after = await RepairAsync(harness).ConfigureAwait(true);

        foreach ((string stored, string expected) in Cases)
        {
            after[stored].ShouldBe(expected, $"the SQL disagreed on {stored}");
            MoneyNumber.Decode(stored).ShouldBe(expected, $"the reader disagreed on {stored}");
        }
    }

    [Fact]
    public async Task Running_the_repair_twice_changes_nothing_the_second_time()
    {
        using var harness = new BookHarness();

        IReadOnlyDictionary<string, string> once = await RepairAsync(harness).ConfigureAwait(true);

        using (MyFinanceDbContext context = harness.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(MoneyNumberRepair.Sql).ConfigureAwait(true);
        }

        // An upgrade is applied once, but a decoded value must not decode again in any case:
        // "1168" would otherwise keep losing its first character.
        (await ReadAsync(harness).ConfigureAwait(true)).ShouldBe(once);
    }

    [Fact]
    public void The_schema_version_covers_the_repair() =>
        // The repair ships as a migration, so a book that has had it applied must say so.
        BookSchema.Current.ShouldBeGreaterThanOrEqualTo(5);

    /// <summary>Plants every case as a transaction, runs the SQL, and reads the numbers back.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> RepairAsync(BookHarness harness)
    {
        int accountId = await harness.AddAccountAsync("Repair").ConfigureAwait(true);

        using (MyFinanceDbContext context = harness.CreateContext())
        {
            int day = 1;

            foreach ((string stored, _) in Cases)
            {
                context.Transactions.Add(new Transaction
                {
                    AccountId = accountId,
                    Date = new DateOnly(2020, 1, day++),
                    Number = stored,
                    Amount = Money.Zero,
                    SequenceInDay = 0,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    ModifiedUtc = DateTimeOffset.UtcNow,
                });
            }

            await context.SaveChangesAsync().ConfigureAwait(true);
            await context.Database.ExecuteSqlRawAsync(MoneyNumberRepair.Sql).ConfigureAwait(true);
        }

        return await ReadAsync(harness).ConfigureAwait(true);
    }

    /// <summary>What each planted value now reads as, keyed by the value it started as.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> ReadAsync(BookHarness harness)
    {
        using MyFinanceDbContext context = harness.CreateContext();

        List<(DateOnly Date, string? Number)> rows = await context.Transactions
            .OrderBy(t => t.Date)
            .Select(t => new ValueTuple<DateOnly, string?>(t.Date, t.Number))
            .ToListAsync()
            .ConfigureAwait(true);

        // Keyed back to the original by planting order, since the value itself has changed.
        return rows
            .Select((row, index) => (Stored: Cases[index].Stored, Now: row.Number ?? string.Empty))
            .ToDictionary(pair => pair.Stored, pair => pair.Now, StringComparer.Ordinal);
    }
}
