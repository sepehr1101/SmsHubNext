using System.Text.Json;
using SmsHubNext.Deployment;
using Xunit;

namespace SmsHubNext.UnitTests.Deployment;

public sealed class SetupCommandRunnerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SmsHubNext.CommandTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Non_setup_arguments_are_not_handled()
    {
        int? exitCode = await SetupCommandRunner.TryRunAsync(["--urls", "http://localhost:5000"]);

        Assert.Null(exitCode);
    }

    [Fact]
    public async Task Unknown_command_returns_machine_readable_failure()
    {
        Directory.CreateDirectory(_directory);
        string resultPath = Path.Combine(_directory, "result.json");

        int? exitCode = await SetupCommandRunner.TryRunAsync(
            ["setup", "unknown", "--result", resultPath]);

        Assert.Equal(SetupExitCodes.InvalidArguments, exitCode);
        SetupCommandResult result = ReadResult(resultPath);
        Assert.False(result.Success);
        Assert.Equal("setup.unknown_command", result.Code);
    }

    [Fact]
    public async Task Invalid_request_never_echoes_password_to_result()
    {
        Directory.CreateDirectory(_directory);
        string requestPath = Path.Combine(_directory, "request.json");
        string resultPath = Path.Combine(_directory, "result.json");
        const string password = "DO-NOT-ECHO-this-password";
        await File.WriteAllTextAsync(
            requestPath,
            $$"""
            {
              "server": "",
              "database": "SmsHubNext",
              "authentication": "SqlServer",
              "username": "sa",
              "password": "{{password}}"
            }
            """);

        int? exitCode = await SetupCommandRunner.TryRunAsync(
            ["setup", "test-database", "--request", requestPath, "--result", resultPath]);

        Assert.Equal(SetupExitCodes.InvalidRequest, exitCode);
        Assert.DoesNotContain(password, await File.ReadAllTextAsync(resultPath), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static SetupCommandResult ReadResult(string path) =>
        JsonSerializer.Deserialize<SetupCommandResult>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
