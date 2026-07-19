using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using SmsHubNext.Features.Landing;
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

        bool openApiEnabled = app.Configuration.GetValue("OpenApi:Enabled", true);
        if (openApiEnabled)
        {
            app.MapOpenApi();
            // Scalar API reference UI, rendering the OpenAPI document above.
            app.MapScalarApiReference();
        }

        // Human-friendly production landing page. The machine-readable service
        // metadata remains available separately for diagnostics and tooling.
        app.MapGet("/", () => ServiceLandingPage.Render(openApiEnabled)).ExcludeFromDescription();
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

        // API-key enforcement is implemented but intentionally inactive (ADR-015): the
        // APIs stay open for testing. To turn it on for every endpoint downstream, add
        //     app.UseApiKeyAuthentication();
        // here, just before MapControllers. The resolver is testable now via /auth/whoami.
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        MapHealthEndpoints(app);

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
