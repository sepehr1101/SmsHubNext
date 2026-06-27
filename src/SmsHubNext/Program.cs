using Serilog;
using SmsHubNext.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Structured logging via Serilog; levels/sinks overridable from configuration.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();

app.MigrateDatabase();
app.ConfigurePipeline();

app.Run();

// Exposed so integration tests can host the app via WebApplicationFactory<Program>.
public partial class Program { }
