using Scalar.AspNetCore;
using Serilog;

namespace SmsHubNext.Extensions;

/// <summary>
/// HTTP pipeline configuration — the middleware/endpoint half of the composition
/// root. Keeps <c>Program.cs</c> minimal (see ARCHITECTURE.md §3).
/// </summary>
public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            // Scalar API reference UI, rendering the OpenAPI document above.
            app.MapScalarApiReference();
        }

        // Root: a quick liveness/landing response listing the useful endpoints.
        app.MapGet("/", () => new
        {
            service = "SmsHubNext",
            status = "ok",
            endpoints = new
            {
                health = "/health",
                openApi = "/openapi/v1.json",
                scalar = "/scalar/v1",
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
            },
        });

        // API-key enforcement is implemented but intentionally inactive (ADR-015): the
        // APIs stay open for testing. To turn it on for every endpoint downstream, add
        //     app.UseApiKeyAuthentication();
        // here, just before MapControllers. The resolver is testable now via /auth/whoami.

        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}
