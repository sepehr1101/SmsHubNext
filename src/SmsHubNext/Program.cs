using Scalar.AspNetCore;
using Serilog;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Features.Sending;
using SmsHubNext.Features.Tariffs;
using SmsHubNext.Shared.Database;

var builder = WebApplication.CreateBuilder(args);

// Structured logging via Serilog; levels/sinks overridable from configuration.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// MVC controllers (feature controllers live under Features/*; see ADR-004).
builder.Services.AddControllers();

// OpenAPI document (exposed at /openapi/v1.json in Development).
builder.Services.AddOpenApi();

// Database access (concrete, no interface — see ARCHITECTURE.md §5).
// Read the connection string here so misconfiguration fails fast at startup.
var connectionString = builder.Configuration.GetConnectionString(Db.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"Connection string '{Db.ConnectionStringName}' is not configured.");
builder.Services.AddSingleton(new Db(connectionString));

// Feature handlers (plain classes, resolved per request).
builder.Services.AddScoped<SendMessagesHandler>();
builder.Services.AddScoped<ListMessageTypesHandler>();
builder.Services.AddScoped<ListProvidersHandler>();
builder.Services.AddScoped<CreateProviderHandler>();
builder.Services.AddScoped<ListSenderLinesHandler>();
builder.Services.AddScoped<ListGeoSectionsHandler>();
builder.Services.AddScoped<CreateGeoSectionHandler>();
builder.Services.AddScoped<CreateCustomerHandler>();
builder.Services.AddScoped<ListCustomersHandler>();
builder.Services.AddScoped<IssueApiKeyHandler>();
builder.Services.AddScoped<ListApiKeysHandler>();
builder.Services.AddScoped<AddIpRestrictionHandler>();
builder.Services.AddScoped<ListIpRestrictionsHandler>();
builder.Services.AddScoped<ListTariffsHandler>();
builder.Services.AddScoped<QuoteHandler>();
builder.Services.AddScoped<GetBalanceHandler>();
builder.Services.AddScoped<TopUpHandler>();
builder.Services.AddScoped<ListTransactionsHandler>();

// Health checks: a database readiness probe (more added as dependencies arrive).
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sql-server");

var app = builder.Build();

// Apply forward-only database migrations at startup (idempotent; fail fast).
var migration = new DatabaseMigrator(connectionString).Migrate();
if (!migration.Successful)
    throw new InvalidOperationException("Database migration failed.", migration.Error);

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
    },
});

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed so integration tests can host the app via WebApplicationFactory<Program>.
public partial class Program { }
