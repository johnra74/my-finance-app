using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace MyFinance.Data.Security;

/// <summary>
/// Turns the user's password into the SQLCipher database key using Argon2id.
/// </summary>
/// <remarks>
/// A memory-hard KDF is the point. The threat here is an attacker who has copied the book
/// file and can guess offline at whatever rate their hardware allows; Argon2id's memory cost
/// is what denies them the GPU parallelism that makes such guessing cheap.
/// </remarks>
public static class BookKeyDerivation
{
    /// <summary>Key size SQLCipher expects for a raw key: 256 bits.</summary>
    public const int KeyBytes = 32;

    public const int SaltBytes = 16;

    /// <summary>
    /// Defaults chosen to cost roughly half a second on a typical desktop. Deliberately
    /// noticeable: this cost is paid once at unlock, and every millisecond of it is
    /// multiplied across an attacker's entire guess space.
    /// </summary>
    public const int DefaultIterations = 4;

    public const int DefaultMemoryKib = 65536; // 64 MiB

    public const int DefaultParallelism = 2;

    /// <summary>Creates fresh parameters with a random salt for a new book.</summary>
    public static BookKeyParameters CreateParameters(
        int iterations = DefaultIterations,
        int memoryKib = DefaultMemoryKib,
        int parallelism = DefaultParallelism,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(memoryKib, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);

        return new BookKeyParameters
        {
            SaltBase64 = Convert.ToBase64String(salt),
            Iterations = iterations,
            MemoryKib = memoryKib,
            Parallelism = parallelism,
            KeyBytes = KeyBytes,
            CreatedUtc = (timeProvider ?? TimeProvider.System).GetUtcNow(),
        };
    }

    /// <summary>Derives the database key for a password under the given parameters.</summary>
    public static BookKey DeriveKey(string password, BookKeyParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(parameters);

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = parameters.GetSalt(),
                Iterations = parameters.Iterations,
                MemorySize = parameters.MemoryKib,
                DegreeOfParallelism = parameters.Parallelism,
            };

            return new BookKey(argon2.GetBytes(parameters.KeyBytes));
        }
        finally
        {
            // The password string itself is immutable and beyond our reach, but the byte
            // copy we made is not.
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
