using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using SmsHubNext.Features.Administration;

namespace SmsHubNext.Extensions;

public static class LoggingExtensions
{
    public static WebApplicationBuilder AddApplicationLogging(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ApplicationLogOptions>(builder.Configuration.GetSection(ApplicationLogOptions.SectionName));
        builder.Host.UseSerilog((context, configuration) =>
        {
            ApplicationLogOptions options = context.Configuration.GetSection(ApplicationLogOptions.SectionName)
                .Get<ApplicationLogOptions>() ?? new();
            configuration.ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(new JsonFormatter(renderMessage: true),
                    Path.Combine(options.ResolveDirectory(context.HostingEnvironment), "errors-.jsonl"),
                    restrictedToMinimumLevel: LogEventLevel.Error,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 32 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 62,
                    shared: true);
        });
        return builder;
    }
}
