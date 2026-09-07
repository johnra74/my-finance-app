using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyFinance.Data;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time.
/// </summary>
/// <remarks>
/// Migration scaffolding only needs the model shape, never real data, so this deliberately
/// points at a throwaway path and involves no encryption or password.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MyFinanceDbContext>
{
    public MyFinanceDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<MyFinanceDbContext> options =
            new DbContextOptionsBuilder<MyFinanceDbContext>()
                .UseSqlite("Data Source=design-time.mfdb")
                .Options;

        return new MyFinanceDbContext(options);
    }
}
