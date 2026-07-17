using Microsoft.AspNetCore.Cors.Infrastructure;
using SmsHubNext.Shared.Http;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Http;

public sealed class ApplicationCorsOptionsTests
{
    [Fact]
    public void Disabled_cors_does_not_require_origins()
    {
        ApplicationCorsOptions options = new();

        options.Validate();
    }

    [Fact]
    public void Enabled_cors_accepts_explicit_http_and_https_origins()
    {
        ApplicationCorsOptions options = new()
        {
            Enabled = true,
            AllowedOrigins = ["http://localhost:5173", "https://panel.example.com/"],
        };

        options.Validate();
    }

    [Fact]
    public void Policy_contains_normalized_origins_headers_methods_and_preflight_settings()
    {
        ApplicationCorsOptions options = new()
        {
            Enabled = true,
            AllowedOrigins = ["https://panel.example.com/"],
            AllowedMethods = ["GET", "POST"],
            AllowedHeaders = ["Authorization", "X-Api-Key"],
            AllowCredentials = true,
            PreflightMaxAgeSeconds = 900,
        };

        CorsPolicy policy = ApplicationCorsPolicy.Create(options);

        Assert.Equal(["https://panel.example.com"], policy.Origins);
        Assert.Equal(["GET", "POST"], policy.Methods);
        Assert.Equal(["Authorization", "X-Api-Key"], policy.Headers);
        Assert.True(policy.SupportsCredentials);
        Assert.Equal(TimeSpan.FromSeconds(900), policy.PreflightMaxAge);
    }

    [Fact]
    public void Enabled_cors_requires_at_least_one_origin()
    {
        ApplicationCorsOptions options = new() { Enabled = true };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("AllowedOrigins", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("panel.example.com")]
    [InlineData("https://panel.example.com/app")]
    [InlineData("https://user@panel.example.com")]
    public void Enabled_cors_rejects_unsafe_or_invalid_origins(string origin)
    {
        ApplicationCorsOptions options = new()
        {
            Enabled = true,
            AllowedOrigins = [origin],
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(86401)]
    public void Preflight_max_age_must_stay_in_supported_range(int seconds)
    {
        ApplicationCorsOptions options = new()
        {
            Enabled = true,
            AllowedOrigins = ["https://panel.example.com"],
            PreflightMaxAgeSeconds = seconds,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
