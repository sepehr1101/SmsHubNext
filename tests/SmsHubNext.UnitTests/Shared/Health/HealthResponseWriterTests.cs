using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using SmsHubNext.Shared.Health;
using SmsHubNext.UnitTests.Shared;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Health;

public sealed class HealthResponseWriterTests
{
    [Fact]
    public async Task Writes_stable_dashboard_json_without_exception_details()
    {
        ManualTimeProvider clock = new(
            new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        HealthResponseWriter writer = new(clock, new TestHostEnvironment());
        clock.Advance(TimeSpan.FromSeconds(12));

        Dictionary<string, object> checkData = new()
        {
            ["latencyMs"] = 8.5d,
        };
        HealthReport report = new(
            new Dictionary<string, HealthReportEntry>
            {
                ["sql-server"] = new HealthReportEntry(
                    HealthStatus.Healthy,
                    "SQL Server is healthy.",
                    TimeSpan.FromMilliseconds(8.5),
                    new InvalidOperationException("must-not-leak"),
                    checkData,
                    [HealthCheckTags.Ready]),
            },
            TimeSpan.FromMilliseconds(9));
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        await writer.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using JsonDocument json = await JsonDocument.ParseAsync(context.Response.Body);
        JsonElement root = json.RootElement;
        Assert.Equal("SmsHubNext", root.GetProperty("service").GetString());
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.Equal(12, root.GetProperty("uptimeSeconds").GetInt64());
        Assert.Equal("sql-server", root.GetProperty("checks")[0].GetProperty("name").GetString());
        Assert.Equal(8.5d, root.GetProperty("checks")[0].GetProperty("data").GetProperty("latencyMs").GetDouble());
        Assert.DoesNotContain("must-not-leak", root.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SmsHubNext";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Test";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
