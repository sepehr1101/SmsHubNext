using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SmsHubNext.Extensions;
using SmsHubNext.Features.Providers;
using Xunit;

namespace SmsHubNext.UnitTests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
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

    private static IConfiguration Configuration()
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
        };

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
