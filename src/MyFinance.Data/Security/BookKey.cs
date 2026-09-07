using System.Security.Cryptography;

namespace MyFinance.Data.Security;

/// <summary>
/// A derived database encryption key, held only for as long as the book is unlocked.
/// </summary>
/// <remarks>
/// The key material is zeroed on disposal. That is not a defence against a determined
/// attacker with debugger access to a running process — it narrows the window in which the
/// key sits in a heap that could end up in a crash dump or a swap file.
/// </remarks>
public sealed class BookKey : IDisposable
{
    private readonly byte[] _key;
    private bool _disposed;

    internal BookKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
        {
            throw new ArgumentException("Key must not be empty.", nameof(key));
        }

        _key = key;
    }

    public int Length => _key.Length;

    /// <summary>
    /// Lowercase hex, for SQLCipher's raw-key pragma. Handing SQLCipher a raw key skips its
    /// own internal KDF, so the only key stretching applied is our Argon2id pass.
    /// </summary>
    internal string ToHex()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Convert.ToHexStringLower(_key);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
