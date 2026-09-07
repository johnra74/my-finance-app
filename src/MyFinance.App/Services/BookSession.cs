using MyFinance.Data;
using MyFinance.Data.Security;

namespace MyFinance.App.Services;

/// <inheritdoc cref="IBookSession" />
public sealed class BookSession : IBookSession, IDisposable
{
    private readonly Lock _gate = new();
    private Book? _book;

    public bool IsUnlocked
    {
        get
        {
            lock (_gate)
            {
                return _book is not null;
            }
        }
    }

    public string? DatabasePath
    {
        get
        {
            lock (_gate)
            {
                return _book?.DatabasePath;
            }
        }
    }

    public Book? Book
    {
        get
        {
            lock (_gate)
            {
                return _book;
            }
        }
    }

    public string? BookName
    {
        get
        {
            string? path = DatabasePath;
            return path is null ? null : Path.GetFileNameWithoutExtension(path);
        }
    }

    public event EventHandler<BookSessionChangedEventArgs>? Changed;

    public void Create(string databasePath, string password)
    {
        Book book = BookFileService.Create(databasePath, password);
        Adopt(book);
    }

    public void Open(string databasePath, string password)
    {
        Book book = BookFileService.Open(databasePath, password);
        Adopt(book);
    }

    public void Lock()
    {
        lock (_gate)
        {
            if (_book is null)
            {
                return;
            }

            _book.Dispose();
            _book = null;
        }

        Changed?.Invoke(this, new BookSessionChangedEventArgs { IsUnlocked = false });
    }

    public MyFinanceDbContext CreateContext()
    {
        lock (_gate)
        {
            return _book is null
                ? throw new InvalidOperationException("No book is open.")
                : _book.CreateContext();
        }
    }

    public void Dispose() => Lock();

    private void Adopt(Book book)
    {
        lock (_gate)
        {
            _book?.Dispose();
            _book = book;
        }

        Changed?.Invoke(this, new BookSessionChangedEventArgs { IsUnlocked = true });
    }
}
