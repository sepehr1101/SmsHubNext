using System.Security.Cryptography;
using System.Text;

namespace SmsHubNext.Shared.Security;

/// <summary>
/// Hashes customer API keys for storage and lookup. Keys are persisted as a
/// SHA-256 hash only — never in plaintext (CLAUDE.md §3, README auth model).
/// The hash is deterministic so an incoming key can be hashed and matched against
/// the stored value.
/// </summary>
public static class ApiKeyHasher
{
    /// <summary>Lowercase hex SHA-256 of the key — the value to store and to look up by.</summary>
    public static string Hash(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// True when <paramref name="apiKey"/> hashes to <paramref name="expectedHash"/>.
    /// The comparison runs in fixed time so it does not leak how much of the hash
    /// matched through timing.
    /// </summary>
    public static bool Matches(string apiKey, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(expectedHash))
            return false;

        var actualHash = Hash(apiKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualHash),
            Encoding.UTF8.GetBytes(expectedHash));
    }
}
