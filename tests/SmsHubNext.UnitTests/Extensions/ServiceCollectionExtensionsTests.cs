using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmsHubNext.Extensions;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.SmsIr;
using Xunit;

namespace SmsHubNext.UnitTests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Default_management_policy_requires_administrator_api_key_claims()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddApplicationServices(Configuration(), Environment(Environments.Development));

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        AuthorizationOptions options = serviceProvider
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value;
        AuthorizationPolicy? administrator = options.GetPolicy(AuthenticationPolicies.Administrator);

        Assert.NotNull(administrator);
        Assert.Same(administrator, options.DefaultPolicy);
        Assert.Contains(
            administrator.Requirements,
            requirement => requirement is RolesAuthorizationRequirement roles &&
                roles.AllowedRoles.Contains(AuthenticationPolicies.AdministratorRole));
        Assert.Contains(
            administrator.Requirements,
            requirement => requirement is ClaimsAuthorizationRequirement claim &&
                claim.ClaimType == AuthenticationClaimTypes.ApiKeyId);
    }

    [Fact]
    public void Development_without_a_real_provider_registers_loopback()
    {
        ServiceCollection services = new ServiceCollection();
        IConfiguration configuration = Configuration();

        services.AddApplicationServices(configuration, Environment(Environments.Development));

        ServiceDescriptor? provider = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(ISmsProvider));
        Assert.NotNull(provider);
        Assert.Equal(typeof(LoopbackSmsProvider), provider.ImplementationType);
    }

    [Fact]
    public void Production_without_a_real_provider_fails_fast()
    {
        ServiceCollection services = new ServiceCollection();
        IConfiguration configuration = Configuration();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddApplicationServices(configuration, Environment(Environments.Production)));

        Assert.Contains("real SMS provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_with_smsir_registers_the_real_provider()
    {
        ServiceCollection services = new ServiceCollection();
        Dictionary<string, string?> overrides = new Dictionary<string, string?>
        {
            ["Providers:SmsIr:Enabled"] = "true",
        };
        IConfiguration configuration = Configuration(overrides);

        services.AddApplicationServices(configuration, Environment(Environments.Production));

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        ISmsProvider provider = Assert.Single(serviceProvider.GetServices<ISmsProvider>());
        Assert.IsType<SmsIrSmsProvider>(provider);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Smsir_rejects_batch_sizes_outside_the_official_limit(int batchSize)
    {
        ServiceCollection services = new ServiceCollection();
        Dictionary<string, string?> overrides = new Dictionary<string, string?>
        {
            ["Providers:SmsIr:Enabled"] = "true",
            ["Providers:SmsIr:BatchSize"] = batchSize.ToString(),
        };
        IConfiguration configuration = Configuration(overrides);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddApplicationServices(configuration, Environment(Environments.Production)));

        Assert.Contains("BatchSize", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Smsir_rejects_a_negative_minimum_request_interval()
    {
        ServiceCollection services = new ServiceCollection();
        Dictionary<string, string?> overrides = new Dictionary<string, string?>
        {
            ["Providers:SmsIr:Enabled"] = "true",
            ["Providers:SmsIr:MinimumRequestInterval"] = "-00:00:01",
        };
        IConfiguration configuration = Configuration(overrides);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddApplicationServices(configuration, Environment(Environments.Production)));

        Assert.Contains("MinimumRequestInterval", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SmsHubNext"] =
                "Server=localhost;Database=SmsHubNextTests;User Id=sa;Password=NotUsed123!;TrustServerCertificate=True",
            ["BearerTokens:Key"] = new string('k', 64),
            ["BearerTokens:Issuer"] = "tests",
            ["BearerTokens:Audience"] = "tests",
            ["BearerTokens:AccessTokenExpirationMinutes"] = "15",
            ["BearerTokens:RefreshTokenExpirationMinutes"] = "60",
            ["Providers:Magfa:Enabled"] = "false",
            ["Providers:Kavenegar:Enabled"] = "false",
            ["Providers:SmsIr:Enabled"] = "false",
        };

        if (overrides is not null)
        {
            foreach ((string key, string? value) in overrides)
                values[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment
    {
        EnvironmentName = name,
        ApplicationName = "SmsHubNext.UnitTests",
        ContentRootPath = AppContext.BaseDirectory,
        ContentRootFileProvider = new NullFileProvider(),
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
