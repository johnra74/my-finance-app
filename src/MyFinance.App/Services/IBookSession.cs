using MyFinance.Data;
using MyFinance.Data.Security;

namespace MyFinance.App.Services;

/// <summary>Raised when the book is locked or unlocked so views can react.</summary>
public sealed class BookSessionChangedEventArgs : EventArgs
{
    public required bool IsUnlocked { get; init; }
}

/// <summary>
/// Holds the currently unlocked book for the lifetime of the application session.
/// </summary>
/// <remarks>
/// Single-user desktop app: exactly one book is open at a time, so this is a singleton
/// rather than something threaded through every call site.
/// </remarks>
public interface IBookSession : IBookContextFactory
{
    bool IsUnlocked { get; }

    /// <summary>Path of the open book, or null when locked.</summary>
    string? DatabasePath { get; }

    /// <summary>
    /// The open book itself, or null when locked.
    /// </summary>
    /// <remarks>
    /// Most callers want only a context and should depend on <see cref="IBookContextFactory" />.
    /// Backing up needs the book: it has to checkpoint the write-ahead log and copy the file
    /// pair, neither of which is expressible as a query.
    /// </remarks>
    Book? Book { get; }

    /// <summary>Display name of the open book, derived from its file name.</summary>
    string? BookName { get; }

    event EventHandler<BookSessionChangedEventArgs>? Changed;

    /// <summary>Creates a new encrypted book and opens it.</summary>
    void Create(string databasePath, string password);

    /// <summary>Unlocks an existing book. Throws <see cref="IncorrectPasswordException"/> on a bad password.</summary>
    void Open(string databasePath, string password);

    /// <summary>Locks the book and zeroes the key.</summary>
    void Lock();

    /// <summary>
    /// Creates a short-lived context against the open book. Callers must dispose it.
    /// </summary>
    /// <remarks>
    /// Declared by <see cref="IBookContextFactory"/>, which is what the data services depend
    /// on: they need somewhere to get a context and nothing else about the session.
    /// </remarks>
    new MyFinanceDbContext CreateContext();
}
