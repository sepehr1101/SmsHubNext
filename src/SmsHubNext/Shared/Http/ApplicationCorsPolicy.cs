using Microsoft.AspNetCore.Cors.Infrastructure;

namespace SmsHubNext.Shared.Http;

public static class ApplicationCorsPolicy
{
    public static CorsPolicy Create(ApplicationCorsOptions options)
    {
        options.Validate();

        CorsPolicyBuilder builder = new();
        if (!options.Enabled)
            return builder.Build();

        string[] origins = options.AllowedOrigins
            .Select(origin => origin.TrimEnd('/'))
            .ToArray();

        builder
            .WithOrigins(origins)
            .WithMethods(options.AllowedMethods)
            .WithHeaders(options.AllowedHeaders)
            .SetPreflightMaxAge(TimeSpan.FromSeconds(options.PreflightMaxAgeSeconds));

        if (options.AllowCredentials)
            builder.AllowCredentials();

        return builder.Build();
    }
}
