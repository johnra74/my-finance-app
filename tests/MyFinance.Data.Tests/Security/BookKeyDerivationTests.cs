using MyFinance.Data.Security;

namespace MyFinance.Data.Tests.Security;

public class BookKeyDerivationTests
{
    private static BookKeyParameters Fast() => TempBook.FastParameters();

    [Fact]
    public void The_same_password_and_salt_always_derive_the_same_key()
    {
        BookKeyParameters parameters = Fast();

        using BookKey first = BookKeyDerivation.DeriveKey("hunter2", parameters);
        using BookKey second = BookKeyDerivation.DeriveKey("hunter2", parameters);

        first.ToHex().ShouldBe(second.ToHex());
    }

    [Fact]
    public void Different_passwords_derive_different_keys()
    {
        BookKeyParameters parameters = Fast();

        using BookKey first = BookKeyDerivation.DeriveKey("hunter2", parameters);
        using BookKey second = BookKeyDerivation.DeriveKey("hunter3", parameters);

        first.ToHex().ShouldNotBe(second.ToHex());
    }

    [Fact]
    public void The_same_password_under_a_different_salt_derives_a_different_key()
    {
        using BookKey first = BookKeyDerivation.DeriveKey("hunter2", Fast());
        using BookKey second = BookKeyDerivation.DeriveKey("hunter2", Fast());

        // This is the whole point of a per-book salt: two books sharing a password must not
        // share a key, so cracking one reveals nothing about the other.
        first.ToHex().ShouldNotBe(second.ToHex());
    }

    [Fact]
    public void Each_new_parameter_set_gets_a_fresh_random_salt()
    {
        string[] salts = Enumerable.Range(0, 20)
            .Select(_ => BookKeyDerivation.CreateParameters().SaltBase64)
            .ToArray();

        salts.Distinct().Count().ShouldBe(salts.Length);
    }

    [Fact]
    public void A_derived_key_is_256_bits()
    {
        using BookKey key = BookKeyDerivation.DeriveKey("hunter2", Fast());

        key.Length.ShouldBe(BookKeyDerivation.KeyBytes);
        key.Length.ShouldBe(32);
        key.ToHex().Length.ShouldBe(64);
    }

    [Fact]
    public void Production_defaults_are_memory_hard()
    {
        BookKeyParameters parameters = BookKeyDerivation.CreateParameters();

        // 64 MiB is what denies an attacker cheap GPU-parallel guessing. If this ever drops,
        // it should be a deliberate decision rather than an accident.
        parameters.MemoryKib.ShouldBeGreaterThanOrEqualTo(65536);
        parameters.Iterations.ShouldBeGreaterThanOrEqualTo(3);
        parameters.Kdf.ShouldBe("argon2id");
    }

    [Fact]
    public void A_disposed_key_can_no_longer_be_read()
    {
        BookKey key = BookKeyDerivation.DeriveKey("hunter2", Fast());
        key.Dispose();

        Should.Throw<ObjectDisposedException>(() => key.ToHex());
    }

    [Fact]
    public void Disposing_a_key_twice_is_harmless()
    {
        BookKey key = BookKeyDerivation.DeriveKey("hunter2", Fast());

        key.Dispose();
        Should.NotThrow(key.Dispose);
    }

    // -- Sidecar serialization -----------------------------------------------------------

    [Fact]
    public void Parameters_round_trip_through_json()
    {
        BookKeyParameters original = BookKeyDerivation.CreateParameters();

        BookKeyParameters restored = BookKeyParameters.FromJson(original.ToJson());

        restored.SaltBase64.ShouldBe(original.SaltBase64);
        restored.Iterations.ShouldBe(original.Iterations);
        restored.MemoryKib.ShouldBe(original.MemoryKib);
        restored.Parallelism.ShouldBe(original.Parallelism);
        restored.KeyBytes.ShouldBe(original.KeyBytes);
    }

    [Fact]
    public void Restored_parameters_derive_the_identical_key()
    {
        BookKeyParameters original = Fast();
        BookKeyParameters restored = BookKeyParameters.FromJson(original.ToJson());

        using BookKey fromOriginal = BookKeyDerivation.DeriveKey("hunter2", original);
        using BookKey fromRestored = BookKeyDerivation.DeriveKey("hunter2", restored);

        fromOriginal.ToHex().ShouldBe(fromRestored.ToHex());
    }

    [Fact]
    public void A_sidecar_from_a_newer_version_is_refused_rather_than_misread()
    {
        string json = BookKeyDerivation.CreateParameters().ToJson()
            .Replace("\"version\": 1", "\"version\": 99");

        Should.Throw<InvalidDataException>(() => BookKeyParameters.FromJson(json));
    }

    [Fact]
    public void A_sidecar_naming_an_unknown_kdf_is_refused()
    {
        string json = BookKeyDerivation.CreateParameters().ToJson()
            .Replace("\"kdf\": \"argon2id\"", "\"kdf\": \"md5\"");

        Should.Throw<InvalidDataException>(() => BookKeyParameters.FromJson(json));
    }

    [Fact]
    public void A_malformed_sidecar_is_refused()
    {
        Should.Throw<Exception>(() => BookKeyParameters.FromJson("{ not json"));
    }

    [Theory]
    [InlineData(0, 8, 1)]
    [InlineData(1, 4, 1)]
    [InlineData(1, 8, 0)]
    public void Nonsensical_cost_parameters_are_refused(int iterations, int memoryKib, int parallelism)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => BookKeyDerivation.CreateParameters(iterations, memoryKib, parallelism));
    }
}
