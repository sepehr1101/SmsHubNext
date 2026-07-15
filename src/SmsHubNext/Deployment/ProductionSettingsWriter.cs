using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.Deployment;

public sealed class ProductionSettingsWriter
{
    private const string DevelopmentSigningKey = "This is my shared key, not so secret, secret!!@#";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public SettingsWriteReceipt Write(
        string settingsPath,
        string connectionString,
        string keyRingPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
            throw new ArgumentException("A production settings path is required.", nameof(settingsPath));

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A connection string is required.", nameof(connectionString));

        if (string.IsNullOrWhiteSpace(keyRingPath))
            throw new ArgumentException("A Data Protection key-ring path is required.", nameof(keyRingPath));

        string fullSettingsPath = Path.GetFullPath(settingsPath);
        string? settingsDirectory = Path.GetDirectoryName(fullSettingsPath);
        if (string.IsNullOrWhiteSpace(settingsDirectory))
            throw new InvalidOperationException("The production settings directory could not be resolved.");

        Directory.CreateDirectory(settingsDirectory);
        JsonObject settings = ReadExistingSettings(fullSettingsPath);
        SetConnectionString(settings, connectionString);
        SetDataProtectionPath(settings, Path.GetFullPath(keyRingPath));
        EnsureProductionSigningKey(settings);

        string temporaryPath = Path.Combine(
            settingsDirectory,
            $".{Path.GetFileName(fullSettingsPath)}.{Guid.NewGuid():N}.tmp");
        string? backupPath = File.Exists(fullSettingsPath)
            ? Path.Combine(settingsDirectory, $".{Path.GetFileName(fullSettingsPath)}.{Guid.NewGuid():N}.bak")
            : null;

        try
        {
            string json = settings.ToJsonString(SerializerOptions) + Environment.NewLine;
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (backupPath is null)
                File.Move(temporaryPath, fullSettingsPath);
            else
                File.Replace(temporaryPath, fullSettingsPath, backupPath, ignoreMetadataErrors: true);

            return new SettingsWriteReceipt(fullSettingsPath, backupPath);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    public void Commit(SettingsWriteReceipt receipt)
    {
        if (receipt.BackupPath is not null)
            File.Delete(receipt.BackupPath);
    }

    public void Rollback(SettingsWriteReceipt receipt)
    {
        if (receipt.BackupPath is null)
        {
            File.Delete(receipt.SettingsPath);
            return;
        }

        File.Copy(receipt.BackupPath, receipt.SettingsPath, overwrite: true);
        File.Delete(receipt.BackupPath);
    }

    private static JsonObject ReadExistingSettings(string settingsPath)
    {
        if (!File.Exists(settingsPath))
            return [];

        string json = File.ReadAllText(settingsPath, Encoding.UTF8);
        JsonNode? node = JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        return node as JsonObject
            ?? throw new InvalidOperationException("Production settings must contain a JSON object at the root.");
    }

    private static void SetConnectionString(JsonObject settings, string connectionString)
    {
        JsonObject connectionStrings = GetOrCreateObject(settings, "ConnectionStrings");
        connectionStrings[Db.ConnectionStringName] = connectionString;
    }

    private static void SetDataProtectionPath(JsonObject settings, string keyRingPath)
    {
        JsonObject dataProtection = GetOrCreateObject(settings, "DataProtection");
        dataProtection["KeyRingPath"] = keyRingPath;
    }

    private static void EnsureProductionSigningKey(JsonObject settings)
    {
        JsonObject bearerTokens = GetOrCreateObject(settings, BearerTokenOptions.SectionName);
        string? existingKey = bearerTokens["Key"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(existingKey) ||
            string.Equals(existingKey, DevelopmentSigningKey, StringComparison.Ordinal))
        {
            bearerTokens["Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        }
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
            return existing;

        JsonObject created = [];
        parent[propertyName] = created;
        return created;
    }
}

public sealed record SettingsWriteReceipt(string SettingsPath, string? BackupPath);
