using SmsHubNext.App.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();

app.ConfigurePipeline();

await app.RunAsync();
