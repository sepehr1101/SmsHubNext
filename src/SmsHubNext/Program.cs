using SmsHubNext.Deployment;
using SmsHubNext.Extensions;

int? setupExitCode = await SetupCommandRunner.TryRunAsync(args);
if (setupExitCode.HasValue)
    return setupExitCode.Value;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Layer the optional gitignored appsettings.{Environment}.local.json (local secrets) over defaults.
builder.AddLocalConfiguration();

builder.AddApplicationLogging();

builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();

app.MigrateDatabase();
await app.SeedDevelopmentDataAsync();
app.ConfigurePipeline();

await app.RunAsync();
return 0;

// Exposed so integration tests can host the app via WebApplicationFactory<Program>.
public partial class Program { }
