using System.Text.Json.Nodes;
using SmsHubNext.Deployment;
using Xunit;

namespace SmsHubNext.UnitTests.Deployment;

public sealed class ProductionSettingsWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SmsHubNext.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Write_merges_production_values_and_preserves_existing_settings()
    {
        Directory.CreateDirectory(_directory);
        string settingsPath = Path.Combine(_directory, "appsettings.Production.json");
        string originalSigningKey = Convert.ToBase64String(new byte[64]);
        File.WriteAllText(
            settingsPath,
            $$"""
            {
              "Custom": { "Keep": true },
              "BearerTokens": { "Key": "{{originalSigningKey}}" }
            }
            """);
        ProductionSettingsWriter writer = new();

        SettingsWriteReceipt receipt = writer.Write(
            settingsPath,
            "Server=.;Database=SmsHubNext;Integrated Security=True",
            Path.Combine(_directory, "keys"));
        writer.Commit(receipt);

        JsonObject settings = ParseSettings(settingsPath);
        Assert.True(settings["Custom"]!["Keep"]!.GetValue<bool>());
        Assert.Equal(originalSigningKey, settings["BearerTokens"]!["Key"]!.GetValue<string>());
        Assert.Contains("SmsHubNext", settings["ConnectionStrings"]!["SmsHubNext"]!.GetValue<string>());
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_directory, "keys")),
            settings["DataProtection"]!["KeyRingPath"]!.GetValue<string>());
        Assert.DoesNotContain(Directory.GetFiles(_directory), file => file.EndsWith(".bak", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_replaces_development_signing_key_with_a_random_production_key()
    {
        Directory.CreateDirectory(_directory);
        string settingsPath = Path.Combine(_directory, "appsettings.Production.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "BearerTokens": {
                "Key": "This is my shared key, not so secret, secret!!@#"
              }
            }
            """);
        ProductionSettingsWriter writer = new();

        SettingsWriteReceipt receipt = writer.Write(settingsPath, "Server=.;Database=SmsHubNext", _directory);
        writer.Commit(receipt);

        string signingKey = ParseSettings(settingsPath)["BearerTokens"]!["Key"]!.GetValue<string>();
        Assert.NotEqual("This is my shared key, not so secret, secret!!@#", signingKey);
        Assert.Equal(64, Convert.FromBase64String(signingKey).Length);
    }

    [Fact]
    public void Rollback_restores_existing_settings_exactly()
    {
        Directory.CreateDirectory(_directory);
        string settingsPath = Path.Combine(_directory, "appsettings.Production.json");
        string original = "{\"Original\":true}";
        File.WriteAllText(settingsPath, original);
        ProductionSettingsWriter writer = new();

        SettingsWriteReceipt receipt = writer.Write(settingsPath, "Server=.;Database=Changed", _directory);
        writer.Rollback(receipt);

        Assert.Equal(original, File.ReadAllText(settingsPath));
    }

    [Fact]
    public void Rollback_removes_new_settings_file()
    {
        string settingsPath = Path.Combine(_directory, "appsettings.Production.json");
        ProductionSettingsWriter writer = new();

        SettingsWriteReceipt receipt = writer.Write(settingsPath, "Server=.;Database=SmsHubNext", _directory);
        writer.Rollback(receipt);

        Assert.False(File.Exists(settingsPath));
    }

    [Fact]
    public void Invalid_existing_json_is_not_overwritten()
    {
        Directory.CreateDirectory(_directory);
        string settingsPath = Path.Combine(_directory, "appsettings.Production.json");
        File.WriteAllText(settingsPath, "not-json");
        ProductionSettingsWriter writer = new();

        Assert.ThrowsAny<Exception>(() => writer.Write(settingsPath, "Server=.;Database=SmsHubNext", _directory));
        Assert.Equal("not-json", File.ReadAllText(settingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static JsonObject ParseSettings(string path) =>
        JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}
