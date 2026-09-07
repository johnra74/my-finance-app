namespace MyFinance.Data;

/// <summary>
/// Hands out short-lived database contexts over the open book.
/// </summary>
/// <remarks>
/// Services depend on this rather than on a context directly. EF change tracking is not
/// thread-safe and a long-lived context accumulates stale entities, so every operation gets
/// its own context, uses it, and disposes it. It also keeps the services free of any
/// knowledge of how the book was unlocked.
/// </remarks>
public interface IBookContextFactory
{
    /// <summary>Creates a context over the open book. The caller owns and must dispose it.</summary>
    MyFinanceDbContext CreateContext();
}
