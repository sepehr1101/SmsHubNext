using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging via Serilog; levels/sinks overridable from configuration.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// MVC controllers (feature controllers live under Features/*; see ADR-004).
builder.Services.AddControllers();

// Liveness probe — fleshed out as real dependencies (SQL Server, providers) arrive.
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed so integration tests can host the app via WebApplicationFactory<Program>.
public partial class Program { }
