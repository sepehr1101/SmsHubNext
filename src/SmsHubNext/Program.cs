using Serilog;
using SmsHubNext.Deployment;
using SmsHubNext.Extensions;

int? setupExitCode = await SetupCommandRunner.TryRunAsync(args);
if (setupExitCode.HasValue)
    return setupExitCode.Value;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Layer the optional gitignored appsettings.{Environment}.local.json (local secrets) over defaults.
builder.AddLocalConfiguration();

// Structured logging via Serilog; levels/sinks overridable from configuration.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddApplicationServices(builder.Configuration);

WebApplication app = builder.Build();

app.MigrateDatabase();
app.ConfigurePipeline();

await app.RunAsync();
return 0;

// Exposed so integration tests can host the app via WebApplicationFactory<Program>.
public partial class Program { }
