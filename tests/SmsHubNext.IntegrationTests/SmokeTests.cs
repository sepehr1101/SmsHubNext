using Xunit;

namespace SmsHubNext.IntegrationTests;

// Placeholder proving the integration-test project compiles against the app.
// Real tests spin up SQL Server (Testcontainers) and stub Magfa (WireMock),
// hosting the app via WebApplicationFactory<Program> once features land.
public class SmokeTests
{
    [Fact]
    public void App_entrypoint_is_referenced()
    {
        Assert.NotNull(typeof(Program));
    }
}
