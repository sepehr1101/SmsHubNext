using Xunit;

namespace SmsHubNext.IntegrationTests.Shared;

/// <summary>
/// Opt-in guard for tests that contact a real third-party provider endpoint. These tests stay
/// skipped during every normal unit/integration test run.
/// </summary>
public sealed class LiveProviderFactAttribute : FactAttribute
{
    public const string EnableVariable = "SMSHUBNEXT_RUN_LIVE_INVALID_CREDENTIALS";

    public LiveProviderFactAttribute()
    {
        string? enabled = Environment.GetEnvironmentVariable(EnableVariable);
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Live provider traffic is disabled. Set {EnableVariable}=true through the guarded runner.";
        }
    }
}
