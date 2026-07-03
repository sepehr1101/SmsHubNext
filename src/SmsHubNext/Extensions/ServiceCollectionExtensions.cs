using System.Net.Http.Headers;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.DeliveryReports;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Inbound;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Magfa;
using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.Extensions;

/// <summary>
/// Service registration for the application — the DI half of the composition root.
/// Keeps <c>Program.cs</c> minimal (see ARCHITECTURE.md §3).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MVC controllers (feature controllers live under Features/*; see ADR-004).
        services.AddControllers();

        // OpenAPI document (exposed at /openapi/v1.json in Development).
        services.AddOpenApi();

        // Database access (concrete, no interface — see ARCHITECTURE.md §5).
        // Read the connection string here so misconfiguration fails fast at startup.
        string connectionString = configuration.GetConnectionString(Db.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{Db.ConnectionStringName}' is not configured.");
        services.AddSingleton(new Db(connectionString));

        services.AddFeatureHandlers();
        services.AddBackgroundDispatch(configuration);
        services.AddDeliveryReportPolling(configuration);
        services.AddInboundPolling(configuration);

        // Health checks: a database readiness probe (more added as dependencies arrive).
        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("sql-server");

        return services;
    }

    // Background dispatch: the SMS provider seam, the dispatch logic, and the hosting worker
    // (ARCHITECTURE.md §9). TimeProvider is injected so dispatch timing is testable.
    private static IServiceCollection AddBackgroundDispatch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        // The one real seam: the active provider. Magfa is used when configured and enabled;
        // otherwise the loopback stand-in keeps dev/local runs and tests working credential-free.
        AddSmsProvider(services, configuration);

        DispatchOptions dispatchOptions = configuration.GetSection(DispatchOptions.SectionName).Get<DispatchOptions>()
            ?? new DispatchOptions();
        services.AddSingleton(dispatchOptions);

        services.AddScoped<MessageDispatcher>();
        services.AddSingleton<SmsProviderRegistry>();
        services.AddHostedService<DispatchWorker>();

        return services;
    }

    // Delivery-report polling: the SQL-backed queue poller and its hosting worker (Phase 2).
    // Shares the ISmsProvider seam and TimeProvider registered by AddBackgroundDispatch.
    private static IServiceCollection AddDeliveryReportPolling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        DeliveryReportPollOptions options =
            configuration.GetSection(DeliveryReportPollOptions.SectionName).Get<DeliveryReportPollOptions>()
            ?? new DeliveryReportPollOptions();
        services.AddSingleton(options);

        services.AddScoped<DeliveryReportPoller>();
        services.AddHostedService<DeliveryReportPollWorker>();

        return services;
    }

    // Inbound (MO) polling: opt-in, since pulling is destructive at the provider (Phase 4). Shares the
    // ISmsProvider seam; the read API (ListInboundMessagesHandler) is always available regardless.
    private static IServiceCollection AddInboundPolling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        InboundPollOptions options = configuration.GetSection(InboundPollOptions.SectionName).Get<InboundPollOptions>()
            ?? new InboundPollOptions();
        services.AddSingleton(options);

        if (options.Enabled)
        {
            services.AddScoped<InboundPoller>();
            services.AddHostedService<InboundPollWorker>();
        }

        return services;
    }

    // Selects and registers the active ISmsProvider. Magfa is a typed HttpClient (base address and
    // timeout configured here); credentials are per account and set per request by the provider, so
    // the client carries no default auth. When disabled/unconfigured the loopback impl stands in.
    private static void AddSmsProvider(IServiceCollection services, IConfiguration configuration)
    {
        MagfaOptions magfaOptions = configuration.GetSection(MagfaOptions.SectionName).Get<MagfaOptions>()
            ?? new MagfaOptions();
        magfaOptions.Validate();
        services.AddSingleton(magfaOptions);

        if (!magfaOptions.Enabled)
        {
            services.AddSingleton<ISmsProvider, LoopbackSmsProvider>();
            return;
        }

        services.AddSingleton<MagfaAccountResolver>();

        services.AddHttpClient<ISmsProvider, MagfaSmsProvider>(client =>
        {
            client.BaseAddress = new Uri(magfaOptions.BaseUrl);
            client.Timeout = magfaOptions.Timeout;
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
    }

    // Feature handlers — plain classes resolved per request. Rather than list every one (a file that
    // grew with each feature and was easy to forget), scan the application assembly with Scrutor and
    // register each concrete *Handler as scoped, as itself (controllers inject the concrete type).
    // The naming convention is the contract: a new `Features/**/*Handler.cs` is wired automatically.
    // See ADR-017.
    private static IServiceCollection AddFeatureHandlers(this IServiceCollection services)
    {
        // API-key authentication — a shared resolver service, not a use-case handler, so it is
        // registered explicitly. The enforcing middleware (ApiKeyAuthenticationMiddleware) is
        // intentionally NOT added to the pipeline yet; the APIs stay open for testing. See ADR-015.
        services.AddScoped<ApiKeyAuthenticator>();

        services.Scan(scan => scan
            .FromAssemblyOf<SendMessagesHandler>()
            .AddClasses(classes => classes.Where(type => type.Name.EndsWith("Handler")), publicOnly: true)
            .AsSelf()
            .WithScopedLifetime());

        return services;
    }
}
