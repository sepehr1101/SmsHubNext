using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.DeliveryReports;
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

        // Health checks: a database readiness probe (more added as dependencies arrive).
        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("sql-server");

        return services;
    }

    // Feature handlers — plain classes, resolved per request.
    private static IServiceCollection AddFeatureHandlers(this IServiceCollection services)
    {
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
