using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using SecurityEntity = MyFinance.Core.Entities.Security;

namespace MyFinance.Data;

/// <summary>The encrypted book: every account, transaction and setting for one user.</summary>
/// <remarks>
/// This is deliberately the only <see cref="DbContext"/> type in the assembly. A derived
/// context would get its own migration identity, so migrations scaffolded against this type
/// would not be found at runtime.
/// </remarks>
public class MyFinanceDbContext : DbContext
{
    private readonly SqliteConnection? _ownedConnection;

    public MyFinanceDbContext(DbContextOptions<MyFinanceDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Creates a context that also disposes <paramref name="ownedConnection"/>. Used when the
    /// caller constructs the keyed connection itself rather than handing EF a connection
    /// string, which is how SQLCipher keying is applied.
    /// </summary>
    internal MyFinanceDbContext(DbContextOptions<MyFinanceDbContext> options, SqliteConnection ownedConnection)
        : base(options)
    {
        _ownedConnection = ownedConnection;
    }

    public override void Dispose()
    {
        base.Dispose();
        _ownedConnection?.Dispose();
        GC.SuppressFinalize(this);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);

        if (_ownedConnection is not null)
        {
            await _ownedConnection.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Payee> Payees => Set<Payee>();

    public DbSet<PayeeAlias> PayeeAliases => Set<PayeeAlias>();

    public DbSet<PayeeEmbedding> PayeeEmbeddings => Set<PayeeEmbedding>();

    public DbSet<MerchantCodeCategory> MerchantCodeCategories => Set<MerchantCodeCategory>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionSplit> TransactionSplits => Set<TransactionSplit>();

    public DbSet<ScheduledTransaction> ScheduledTransactions => Set<ScheduledTransaction>();

    public DbSet<ScheduledTransactionSplit> ScheduledTransactionSplits => Set<ScheduledTransactionSplit>();

    public DbSet<ScheduleOccurrence> ScheduleOccurrences => Set<ScheduleOccurrence>();

    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();

    public DbSet<WatchedCategory> WatchedCategories => Set<WatchedCategory>();

    public DbSet<CategorizationRule> CategorizationRules => Set<CategorizationRule>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<SecurityEntity> Securities => Set<SecurityEntity>();

    public DbSet<Holding> Holdings => Set<Holding>();

    public DbSet<InvestmentTransaction> InvestmentTransactions => Set<InvestmentTransaction>();

    public DbSet<SecurityPrice> SecurityPrices => Set<SecurityPrice>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Money is a one-field struct over cents, so it maps to a single INTEGER column.
        // Registering it as a convention keeps every amount in the schema exact by default
        // rather than relying on each property remembering to opt in.
        builder.Properties<Money>().HaveConversion<MoneyConverter>();
        builder.Properties<Money?>().HaveConversion<NullableMoneyConverter>();

        // A holding is 12.3456 shares, which cents cannot express and double cannot express
        // exactly. Same treatment for the same reason: one INTEGER, integer arithmetic.
        builder.Properties<Quantity>().HaveConversion<QuantityConverter>();

        base.ConfigureConventions(builder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MyFinanceDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    private sealed class MoneyConverter : ValueConverter<Money, long>
    {
        public MoneyConverter()
            : base(money => money.MinorUnits, cents => Money.FromMinorUnits(cents))
        {
        }
    }

    private sealed class QuantityConverter : ValueConverter<Quantity, long>
    {
        public QuantityConverter()
            : base(quantity => quantity.ScaledUnits, units => Quantity.FromScaledUnits(units))
        {
        }
    }

    private sealed class NullableMoneyConverter : ValueConverter<Money?, long?>
    {
        public NullableMoneyConverter()
            : base(
                money => money == null ? null : money.Value.MinorUnits,
                cents => cents == null ? null : Money.FromMinorUnits(cents.Value))
        {
        }
    }
}
