using SmsHubNext.Shared.Security;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Security;

public class ApiKeyHasherTests
{
    // NIST SHA-256 test vector for the input "abc".
    private const string AbcSha256 =
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void Hash_matches_the_known_sha256_vector()
    {
        Assert.Equal(AbcSha256, ApiKeyHasher.Hash("abc"));
    }

    [Fact]
    public void Hash_is_deterministic()
    {
        Assert.Equal(ApiKeyHasher.Hash("secret-key"), ApiKeyHasher.Hash("secret-key"));
    }

    [Fact]
    public void Hash_differs_for_different_keys()
    {
        Assert.NotEqual(ApiKeyHasher.Hash("key-one"), ApiKeyHasher.Hash("key-two"));
    }

    [Fact]
    public void Hash_is_64_lowercase_hex_chars()
    {
        var hash = ApiKeyHasher.Hash("anything");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_rejects_blank_keys(string apiKey)
    {
        Assert.Throws<ArgumentException>(() => ApiKeyHasher.Hash(apiKey));
    }

    [Fact]
    public void Matches_is_true_for_the_correct_key()
    {
        var hash = ApiKeyHasher.Hash("correct-horse");

        Assert.True(ApiKeyHasher.Matches("correct-horse", hash));
    }

    [Fact]
    public void Matches_is_false_for_a_wrong_key()
    {
        var hash = ApiKeyHasher.Hash("correct-horse");

        Assert.False(ApiKeyHasher.Matches("wrong-key", hash));
    }

    [Theory]
    [InlineData("", "somehash")]
    [InlineData("key", "")]
    public void Matches_is_false_for_blank_inputs(string apiKey, string expectedHash)
    {
        Assert.False(ApiKeyHasher.Matches(apiKey, expectedHash));
    }
}
