using System.Data;
using Microsoft.Data.Sqlite;

namespace MyFinance.Data.Security;

/// <summary>
/// Produces SQLite connections that key themselves against SQLCipher on every open.
/// </summary>
/// <remarks>
/// <para>
/// The pragma is re-applied through a <see cref="DbConnection.StateChange"/> handler rather
/// than once at construction, because EF Core opens and closes the underlying connection on
/// its own schedule; a key applied only at construction would be gone by the second query.
/// </para>
/// <para>
/// The key travels as SQLCipher's raw-key form (<c>x'hex'</c>), which bypasses SQLCipher's
/// own PBKDF2 pass. That is intentional — the stretching already happened in Argon2id, and
/// letting SQLCipher re-derive from a passphrase would substitute a weaker KDF for a
/// stronger one.
/// </para>
/// </remarks>
public sealed class EncryptedConnectionFactory
{
    private readonly string _databasePath;
    private readonly string _keyHex;

    static EncryptedConnectionFactory()
    {
        // Selects the SQLCipher-enabled native provider. Without this, Microsoft.Data.Sqlite
        // may bind a plain SQLite build that silently ignores PRAGMA key.
        SQLitePCL.Batteries_V2.Init();
    }

    public EncryptedConnectionFactory(string databasePath, BookKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(key);

        _databasePath = databasePath;
        _keyHex = key.ToHex();
    }

    public string DatabasePath => _databasePath;

    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
    }.ToString();

    /// <summary>Creates a connection that keys itself whenever it is opened.</summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.StateChange += OnStateChange;
        return connection;
    }

    /// <summary>Creates and opens a keyed connection.</summary>
    public SqliteConnection CreateOpenConnection()
    {
        SqliteConnection connection = CreateConnection();

        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void OnStateChange(object sender, StateChangeEventArgs args)
    {
        if (args.CurrentState != ConnectionState.Open)
        {
            return;
        }

        var connection = (SqliteConnection)sender;
        ApplyKey(connection, _keyHex);
    }

    /// <summary>SQLITE_NOTADB — what SQLCipher returns when the key does not decrypt the file.</summary>
    private const int SqliteNotADatabase = 26;

    internal static void ApplyKey(SqliteConnection connection, string keyHex)
    {
        using SqliteCommand command = connection.CreateCommand();

        // PRAGMA key must be the first statement executed on the connection. It only stores
        // the key; it does not itself touch a page, so it succeeds even for a wrong key.
        command.CommandText = $"PRAGMA key = \"x'{keyHex}'\";";
        command.ExecuteNonQuery();

        try
        {
            // The first statement that actually reads a page is where a wrong key surfaces.
            // WAL keeps the register responsive while a long import writes in the background.
            command.CommandText = "PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();

            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteNotADatabase)
        {
            // Translated here rather than at the call site so that every path which reopens
            // the connection — including EF reopening it mid-session — reports a bad key as
            // a bad key, instead of leaking a raw "file is not a database".
            throw new IncorrectPasswordException();
        }
    }

    /// <summary>
    /// Confirms the key actually decrypts the file. SQLCipher accepts any key at
    /// <c>PRAGMA key</c> time and only fails when it first has to read a page, so an unlock
    /// is not proven until something is actually queried.
    /// </summary>
    internal static bool CanRead(SqliteConnection connection)
    {
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_schema;";
            command.ExecuteScalar();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteNotADatabase)
        {
            return false;
        }
    }
}
