using Serilog;
using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Database;

var builder = WebApplication.CreateBuilder(args);

// Structured logging via Serilog; levels/sinks overridable from configuration.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// MVC controllers (feature controllers live under Features/*; see ADR-004).
builder.Services.AddControllers();

// Database access (concrete, no interface — see ARCHITECTURE.md §5).
// Read the connection string here so misconfiguration fails fast at startup.
builder.Services.AddSingleton(new Db(
    builder.Configuration.GetConnectionString(Db.ConnectionStringName)
        ?? throw new InvalidOperationException(
            $"Connection string '{Db.ConnectionStringName}' is not configured.")));

// Feature handlers (plain classes, resolved per request).
builder.Services.AddScoped<SendMessagesHandler>();

// Health checks: a database readiness probe (more added as dependencies arrive).
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sql-server");

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed so integration tests can host the app via WebApplicationFactory<Program>.
public partial class Program { }
