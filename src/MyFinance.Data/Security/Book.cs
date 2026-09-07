using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MyFinance.Data.Security;

/// <summary>
/// An unlocked book. Holds the derived key for the lifetime of the session and hands out
/// database contexts on demand; disposing it locks the book and zeroes the key.
/// </summary>
public sealed class Book : IBookContextFactory, IDisposable
{
    private readonly BookKey _key;
    private readonly EncryptedConnectionFactory _connections;
    private bool _disposed;

    internal Book(string databasePath, BookKey key, BookKeyParameters parameters)
    {
        DatabasePath = databasePath;
        KeyParameters = parameters;
        _key = key;
        _connections = new EncryptedConnectionFactory(databasePath, key);
    }

    public string DatabasePath { get; }

    public BookKeyParameters KeyParameters { get; }

    /// <summary>
    /// Creates a context over its own keyed connection. Callers own the returned context and
    /// should keep it short-lived, as EF change tracking is not thread-safe.
    /// </summary>
    public MyFinanceDbContext CreateContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        SqliteConnection connection = _connections.CreateConnection();

        DbContextOptions<MyFinanceDbContext> options =
            new DbContextOptionsBuilder<MyFinanceDbContext>()
                .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(typeof(Book).Assembly.FullName))
                .Options;

        return new MyFinanceDbContext(options, connection);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _key.Dispose();
    }
}
