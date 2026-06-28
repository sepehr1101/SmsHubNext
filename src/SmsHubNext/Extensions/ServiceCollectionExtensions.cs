using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.DeliveryReports;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Features.Sending;
using SmsHubNext.Features.Tariffs;
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
        var connectionString = configuration.GetConnectionString(Db.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{Db.ConnectionStringName}' is not configured.");
        services.AddSingleton(new Db(connectionString));

        services.AddFeatureHandlers();
        services.AddBackgroundDispatch(configuration);

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

        // The one real seam — a loopback stand-in until the Magfa client lands (Phase 1).
        services.AddSingleton<ISmsProvider, LoopbackSmsProvider>();

        var dispatchOptions = configuration.GetSection(DispatchOptions.SectionName).Get<DispatchOptions>()
            ?? new DispatchOptions();
        services.AddSingleton(dispatchOptions);

        services.AddScoped<MessageDispatcher>();
        services.AddHostedService<DispatchWorker>();

        return services;
    }

    // Feature handlers — plain classes, resolved per request.
    private static IServiceCollection AddFeatureHandlers(this IServiceCollection services)
    {
        // API-key authentication — resolver service only. The enforcing middleware
        // (ApiKeyAuthenticationMiddleware) is intentionally NOT added to the pipeline yet;
        // the APIs stay open for testing. See ADR-015.
        services.AddScoped<ApiKeyAuthenticator>();

        services.AddScoped<SendMessagesHandler>();

        services.AddScoped<GetBatchHandler>();
        services.AddScoped<ListBatchMessagesHandler>();

        services.AddScoped<IngestDeliveryReportHandler>();
        services.AddScoped<ListDeliveryReportsHandler>();

        services.AddScoped<ListMessageTypesHandler>();
        services.AddScoped<ListProvidersHandler>();
        services.AddScoped<CreateProviderHandler>();
        services.AddScoped<ListSenderLinesHandler>();
        services.AddScoped<ListGeoSectionsHandler>();
        services.AddScoped<CreateGeoSectionHandler>();
        services.AddScoped<CreateCustomerHandler>();
        services.AddScoped<ListCustomersHandler>();

        services.AddScoped<IssueApiKeyHandler>();
        services.AddScoped<ListApiKeysHandler>();
        services.AddScoped<AddIpRestrictionHandler>();
        services.AddScoped<ListIpRestrictionsHandler>();

        services.AddScoped<ListTariffsHandler>();
        services.AddScoped<QuoteHandler>();

        services.AddScoped<GetBalanceHandler>();
        services.AddScoped<TopUpHandler>();
        services.AddScoped<ListTransactionsHandler>();

        return services;
    }
}
