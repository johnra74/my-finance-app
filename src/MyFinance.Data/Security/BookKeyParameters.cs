using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyFinance.Data.Security;

/// <summary>
/// The public half of the key derivation — salt and Argon2 cost parameters — stored beside
/// the encrypted book in a small unencrypted sidecar file.
/// </summary>
/// <remarks>
/// <para>
/// This has to live outside the encrypted database, because it is needed to derive the key
/// that opens it. Nothing here is secret: a salt's job is to be unique, not hidden, and
/// publishing the cost parameters is standard practice. The password itself is never stored
/// in any form, hashed or otherwise.
/// </para>
/// <para>
/// Recording the parameters rather than hard-coding them means costs can be raised for new
/// books over time without stranding books created under the old settings.
/// </para>
/// </remarks>
public sealed record BookKeyParameters
{
    /// <summary>Sidecar file format version, so the layout can change compatibly.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    /// <summary>
    /// The database schema version this book was last written by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the same thing as <see cref="Version"/>: that gates how to read <em>this file</em>,
    /// this gates whether the build may open <em>the database</em> at all.
    /// </para>
    /// <para>
    /// It lives here, in the plaintext sidecar, because this is the only copy that can be read
    /// <em>before</em> the key is derived — so a book from a newer build is refused without
    /// first spending half a second in Argon2id on a password that was never going to help.
    /// The database holds the authoritative copy; this one is the early warning.
    /// </para>
    /// <para>
    /// Null on books written before versioning shipped, which are read as
    /// <see cref="BookSchema.Unstamped"/> rather than as unknown.
    /// </para>
    /// </remarks>
    [JsonPropertyName("schemaVersion")]
    public int? SchemaVersion { get; init; }

    [JsonPropertyName("kdf")]
    public string Kdf { get; init; } = "argon2id";

    /// <summary>Base64 random salt, unique per book.</summary>
    [JsonPropertyName("salt")]
    public required string SaltBase64 { get; init; }

    [JsonPropertyName("iterations")]
    public int Iterations { get; init; }

    [JsonPropertyName("memoryKib")]
    public int MemoryKib { get; init; }

    [JsonPropertyName("parallelism")]
    public int Parallelism { get; init; }

    [JsonPropertyName("keyBytes")]
    public int KeyBytes { get; init; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static BookKeyParameters FromJson(string json)
    {
        BookKeyParameters? parsed = JsonSerializer.Deserialize<BookKeyParameters>(json, SerializerOptions);

        if (parsed is null)
        {
            throw new InvalidDataException("The key parameter sidecar file is empty or malformed.");
        }

        if (parsed.Version != 1)
        {
            throw new InvalidDataException(
                $"Key parameter sidecar version {parsed.Version} is newer than this build understands.");
        }

        if (!string.Equals(parsed.Kdf, "argon2id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported key derivation function '{parsed.Kdf}'.");
        }

        return parsed;
    }

    public byte[] GetSalt() => Convert.FromBase64String(SaltBase64);

    /// <summary>
    /// The schema version this sidecar claims, treating an absent stamp as an unversioned
    /// book rather than as an unknown one.
    /// </summary>
    public int SchemaVersionOrUnstamped => SchemaVersion ?? BookSchema.Unstamped;

    /// <summary>Returns a copy stamped with the given schema version.</summary>
    public BookKeyParameters WithSchemaVersion(int schemaVersion) =>
        this with { SchemaVersion = schemaVersion };
}
