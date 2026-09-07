using MyFinance.Data.Security;

namespace MyFinance.Data.Tests;

/// <summary>
/// A book in a temporary directory, cleaned up on disposal.
/// </summary>
/// <remarks>
/// Argon2 costs are deliberately floored here. The production defaults spend around half a
/// second per unlock by design, which is correct for a real book and ruinous for a test
/// suite that opens dozens of them.
/// </remarks>
internal sealed class TempBook : IDisposable
{
    public const string DefaultPassword = "correct horse battery staple";

    private readonly string _directory;

    public TempBook(string fileName = "test.mfdb")
    {
        _directory = Path.Combine(Path.GetTempPath(), "myfinance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, fileName);
    }

    public string DatabasePath { get; }

    public string MetadataPath => BookFileService.GetMetadataPath(DatabasePath);

    /// <summary>Cheap-to-derive parameters so tests stay fast.</summary>
    public static BookKeyParameters FastParameters() =>
        BookKeyDerivation.CreateParameters(iterations: 1, memoryKib: 8, parallelism: 1);

    public Book Create(string password = DefaultPassword) =>
        BookFileService.Create(DatabasePath, password, FastParameters());

    public Book Open(string password = DefaultPassword) =>
        BookFileService.Open(DatabasePath, password);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file on a failing test must not mask the real assertion failure.
        }
    }
}
