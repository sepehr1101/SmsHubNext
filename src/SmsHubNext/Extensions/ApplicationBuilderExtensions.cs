using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using SmsHubNext.Shared.Health;
using SmsHubNext.Shared.Http;

namespace SmsHubNext.Extensions;

/// <summary>
/// HTTP pipeline configuration — the middleware/endpoint half of the composition
/// root. Keeps <c>Program.cs</c> minimal (see ARCHITECTURE.md §3).
/// </summary>
public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging();

        app.UseRouting();

        ApplicationCorsOptions corsOptions = app.Services.GetRequiredService<ApplicationCorsOptions>();
        if (corsOptions.Enabled)
            app.UseCors(ApplicationCorsOptions.PolicyName);

        app.UseRateLimiter();

        bool openApiEnabled = app.Configuration.GetValue("OpenApi:Enabled", true);
        if (openApiEnabled)
        {
            app.MapOpenApi();
            // Scalar API reference UI, rendering the OpenAPI document above.
            app.MapScalarApiReference();
        }

        // The root is the Blazor landing page; service metadata remains available separately.
        app.MapGet("/service-info", () => new
        {
            service = "SmsHubNext",
            status = "ok",
            documentation = openApiEnabled
                ? new
                {
                    openApi = "/openapi/v1.json",
                    scalar = "/scalar/v1",
                }
                : null,
            endpoints = new
            {
                health = "/health",
                healthLive = "/health/live",
                healthReady = "/health/ready",
                messageTypes = "/reference-data/message-types",
                providers = "/reference-data/providers",
                senderLines = "/reference-data/sender-lines",
                geoSections = "/reference-data/geo-sections",
                customers = "/customers",
                apiKeys = "/api-keys?customerId=1",
                tariffs = "/tariffs",
                quote = "/tariffs/quote",
                balances = "/balances?customerId=1",
                whoami = "/auth/whoami",
                dispatchOperations = "/dispatch/operations/summary",
                factoryReset = "/setup/factory-reset",
            },
        }).ExcludeFromDescription();

        // Management controllers use administrator JWTs. Customer send and administrator
        // token exchange validate X-Api-Key explicitly, so API-key middleware must not be
        // enabled globally here.
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapControllers();
        MapHealthEndpoints(app);
        app.MapManagementUi();

        return app;
    }

    private static void MapHealthEndpoints(WebApplication app)
    {
        HealthResponseWriter responseWriter = app.Services.GetRequiredService<HealthResponseWriter>();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = responseWriter.WriteAsync,
            AllowCachingResponses = false,
        });

        app.MapHealthChecks("/health/ready", CreateReadinessOptions(responseWriter));

        // Compatibility alias used by the installer, smoke tests, landing page, and existing monitors.
        app.MapHealthChecks("/health", CreateReadinessOptions(responseWriter));
    }

    private static HealthCheckOptions CreateReadinessOptions(HealthResponseWriter responseWriter) =>
        new()
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
            ResponseWriter = responseWriter.WriteAsync,
            AllowCachingResponses = false,
        };
}
