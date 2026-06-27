var builder = WebApplication.CreateBuilder(args);

// MVC controllers (feature controllers live under Features/*; see ADR-004).
builder.Services.AddControllers();

// Liveness probe — fleshed out as real dependencies (SQL Server, providers) arrive.
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
