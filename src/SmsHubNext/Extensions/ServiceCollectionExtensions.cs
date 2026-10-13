using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.DeliveryReports;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Inbound;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Kavenegar;
using SmsHubNext.Features.Providers.Magfa;
using SmsHubNext.Features.Providers.Esb;
using SmsHubNext.Features.Providers.Provider4040;
using SmsHubNext.Features.Providers.SmsIr;
using SmsHubNext.Features.Sending;
using SmsHubNext.Deployment;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Errors;
using SmsHubNext.Shared.Health;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Extensions;

/// <summary>
/// Service registration for the application — the DI half of the composition root.
/// Keeps <c>Program.cs</c> minimal (see ARCHITECTURE.md §3).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // MVC controllers (feature controllers live under Features/*; see ADR-004).
        services.AddControllers(options =>
        {
            options.Filters.Add(new AuthorizeFilter(AuthenticationPolicies.Administrator));
        });
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                List<ApiError> errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .SelectMany(entry => entry.Value!.Errors.Select(_ =>
                        new ApiError(
                            entry.Key,
                            "validation.invalid_field",
                            UserMessages.Common.InvalidField(entry.Key))))
                    .ToList();

                ApiResponse<object> response = ApiResponse.Failure(
                    "validation.failed",
                    UserMessages.Common.RequestInvalid,
                    context.HttpContext,
                    errors);

                return new ObjectResult(response) { StatusCode = StatusCodes.Status400BadRequest };
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddApplicationDataProtection(configuration);
        services.AddJwtBearerAuthentication(configuration);
        services.AddAdminTokenRateLimiting();
        services.AddApplicationCors(configuration);

        // OpenAPI document (runtime exposure is controlled by OpenApi:Enabled).
        services.AddOpenApi();

        // Database access (concrete, no interface — see ARCHITECTURE.md §5).
        // Read the connection string here so misconfiguration fails fast at startup.
        string connectionString = configuration.GetConnectionString(Db.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{Db.ConnectionStringName}' is not configured.");
        services.AddSingleton(new Db(connectionString));
        services.AddScoped<DevelopmentDataSeeder>();

        services.AddFeatureHandlers();
        services.AddEsb(configuration);
        services.AddBackgroundDispatch(configuration, environment);
        services.AddDeliveryReportPolling(configuration);
        services.AddInboundPolling(configuration);
        services.AddApplicationHealthChecks();
        services.AddManagementUi(configuration);

        return services;
    }

    private static IServiceCollection AddEsb(this IServiceCollection services, IConfiguration configuration)
    {
        EsbOptions options = configuration.GetSection(EsbOptions.SectionName).Get<EsbOptions>() ?? new EsbOptions();
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<EsbAccessTokenProvider>();
        services.AddHttpClient(EsbOptions.HttpClientName, (provider, client) =>
            client.Timeout = provider.GetRequiredService<EsbOptions>().Timeout);
        if (options.Enabled)
        {
            services.AddSingleton<Provider4040RouteResolver>();
            services.AddHttpClient<ISmsProvider, Provider4040SmsProvider>(client => client.Timeout = options.Timeout);
        }
        return services;
    }

    private static IServiceCollection AddApplicationHealthChecks(this IServiceCollection services)
    {
        string[] readyTags = [HealthCheckTags.Ready];

        services.AddSingleton<BackgroundWorkerHealthMonitor>();
        services.AddSingleton<HealthResponseWriter>();

        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>(
                "sql-server",
                tags: readyTags,
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<StorageHealthCheck>(
                "storage",
                tags: readyTags,
                timeout: TimeSpan.FromSeconds(2))
            .AddCheck<ProcessMemoryHealthCheck>(
                "process-memory",
                tags: readyTags,
                timeout: TimeSpan.FromSeconds(1))
            .AddCheck<ProviderSecretsHealthCheck>(
                "provider-secrets",
                tags: readyTags,
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<BackgroundWorkersHealthCheck>(
                "background-workers",
                tags: readyTags,
                timeout: TimeSpan.FromSeconds(1));

        return services;
    }

    private static IServiceCollection AddApplicationCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ApplicationCorsOptions options = configuration
            .GetSection(ApplicationCorsOptions.SectionName)
            .Get<ApplicationCorsOptions>() ?? new ApplicationCorsOptions();
        CorsPolicy policy = ApplicationCorsPolicy.Create(options);
        services.AddSingleton(options);

        services.AddCors(corsOptions =>
            corsOptions.AddPolicy(ApplicationCorsOptions.PolicyName, policy));

        return services;
    }

    private static IServiceCollection AddApplicationDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IDataProtectionBuilder dataProtection = services.AddDataProtection()
            .SetApplicationName("SmsHubNext");

        string? keyRingPath = configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            DirectoryInfo keyRingDirectory = Directory.CreateDirectory(keyRingPath);
            dataProtection.PersistKeysToFileSystem(keyRingDirectory);

            if (OperatingSystem.IsWindows())
                dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        return services;
    }

    private static IServiceCollection AddJwtBearerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        BearerTokenOptions options = configuration.GetSection(BearerTokenOptions.SectionName).Get<BearerTokenOptions>()
            ?? new BearerTokenOptions();
        options.Validate();
        services.AddSingleton(options);

        byte[] signingKey = Encoding.UTF8.GetBytes(options.Key);
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtOptions =>
            {
                jwtOptions.RequireHttpsMetadata = true;
                jwtOptions.SaveToken = false;
                jwtOptions.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        Error error = Error.Unauthorized(
                            "auth.management_token_required",
                            UserMessages.Auth.AuthenticationRequired);
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(
                            ApiResponse.Failure(error, context.HttpContext),
                            context.HttpContext.RequestAborted);
                    },
                    OnForbidden = async context =>
                    {
                        Error error = Error.Forbidden(
                            "auth.management_access_denied",
                            UserMessages.Auth.AccessDenied);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(
                            ApiResponse.Failure(error, context.HttpContext),
                            context.HttpContext.RequestAborted);
                    },
                };
                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        services.AddAuthorization(authorizationOptions =>
        {
            AuthorizationPolicy administrators = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(AuthenticationPolicies.AdministratorRole)
                .RequireClaim(
                    AuthenticationClaimTypes.AccessLevel,
                    ((byte)ApiKeyAccessLevel.Administrator).ToString())
                .RequireClaim(AuthenticationClaimTypes.ApiKeyId)
                .RequireClaim(AuthenticationClaimTypes.CustomerId)
                .RequireClaim(AuthenticationClaimTypes.KeyPrefix)
                .Build();
            authorizationOptions.DefaultPolicy = administrators;
            authorizationOptions.AddPolicy(AuthenticationPolicies.Administrator, administrators);
        });
        services.AddSingleton<AdminAccessTokenIssuer>();

        return services;
    }

    private static IServiceCollection AddAdminTokenRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(
                AuthenticationPolicies.TokenExchangeRateLimit,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                Error error = Error.Conflict(
                    "auth.too_many_token_requests",
                    UserMessages.Auth.TooManyTokenRequests);
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse.Failure(error, context.HttpContext),
                    cancellationToken);
            };
        });

        return services;
    }

    // Background dispatch: the SMS provider seam, the dispatch logic, and the hosting worker
    // (ARCHITECTURE.md §9). TimeProvider is injected so dispatch timing is testable.
    private static IServiceCollection AddBackgroundDispatch(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton(TimeProvider.System);

        // The one real seam: the active provider. Magfa is used when configured and enabled;
        // otherwise the loopback stand-in keeps dev/local runs and tests working credential-free.
        AddSmsProvider(services, configuration, environment);

        DispatchOptions dispatchOptions = configuration.GetSection(DispatchOptions.SectionName).Get<DispatchOptions>()
            ?? new DispatchOptions();
        dispatchOptions.Validate();
        services.AddSingleton(dispatchOptions);

        services.AddScoped<MessageDispatcher>();
        services.AddSingleton<SmsProviderRegistry>();
        services.AddHostedService<DispatchWorker>();

        return services;
    }

    // Delivery-report polling: the SQL-backed queue poller and its hosting worker (Phase 2).
    // Shares the ISmsProvider seam and TimeProvider registered by AddBackgroundDispatch.
    private static IServiceCollection AddDeliveryReportPolling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        DeliveryReportPollOptions options =
            configuration.GetSection(DeliveryReportPollOptions.SectionName).Get<DeliveryReportPollOptions>()
            ?? new DeliveryReportPollOptions();
        services.AddSingleton(options);

        services.AddScoped<DeliveryReportPoller>();
        if (options.Enabled)
            services.AddHostedService<DeliveryReportPollWorker>();

        return services;
    }

    // Inbound (MO) polling: opt-in, since pulling is destructive at the provider (Phase 4). Shares the
    // ISmsProvider seam; the read API (ListInboundMessagesHandler) is always available regardless.
    private static IServiceCollection AddInboundPolling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        InboundPollOptions options = configuration.GetSection(InboundPollOptions.SectionName).Get<InboundPollOptions>()
            ?? new InboundPollOptions();
        services.AddSingleton(options);

        if (options.Enabled)
        {
            services.AddScoped<InboundPoller>();
            services.AddHostedService<InboundPollWorker>();
        }

        return services;
    }

    // Selects and registers the active ISmsProvider. Real providers use typed HttpClients; credentials
    // are resolved per account and set per request. Loopback is deliberately restricted to Development
    // and Testing so a production configuration error can never look like a successful SMS dispatch.
    private static void AddSmsProvider(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        bool realProviderRegistered = configuration.GetValue<bool>("Esb:Enabled");

        MagfaOptions magfaOptions = configuration.GetSection(MagfaOptions.SectionName).Get<MagfaOptions>()
            ?? new MagfaOptions();
        magfaOptions.Validate();
        services.AddSingleton(magfaOptions);

        if (magfaOptions.Enabled)
        {
            services.AddSingleton<MagfaAccountResolver>();
            services.AddHttpClient<ISmsProvider, MagfaSmsProvider>(client =>
            {
                client.BaseAddress = new Uri(magfaOptions.BaseUrl);
                client.Timeout = magfaOptions.Timeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });
            realProviderRegistered = true;
        }

        KavenegarOptions kavenegarOptions = configuration.GetSection(KavenegarOptions.SectionName).Get<KavenegarOptions>()
            ?? new KavenegarOptions();
        kavenegarOptions.Validate();
        services.AddSingleton(kavenegarOptions);

        if (kavenegarOptions.Enabled)
        {
            services.AddSingleton<KavenegarAccountResolver>();
            services.AddHttpClient<ISmsProvider, KavenegarSmsProvider>(client =>
            {
                client.BaseAddress = new Uri(kavenegarOptions.BaseUrl);
                client.Timeout = kavenegarOptions.Timeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });
            realProviderRegistered = true;
        }

        SmsIrOptions smsIrOptions = configuration.GetSection(SmsIrOptions.SectionName).Get<SmsIrOptions>()
            ?? new SmsIrOptions();
        smsIrOptions.Validate();
        services.AddSingleton(smsIrOptions);

        if (smsIrOptions.Enabled)
        {
            services.AddSingleton<SmsIrAccountResolver>();
            services.AddHttpClient<ISmsProvider, SmsIrSmsProvider>(client =>
            {
                client.BaseAddress = new Uri(smsIrOptions.BaseUrl);
                client.Timeout = smsIrOptions.Timeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });
            realProviderRegistered = true;
        }

        if (!realProviderRegistered &&
            (environment.IsDevelopment() || environment.IsEnvironment("Testing")))
        {
            services.AddSingleton<ISmsProvider, LoopbackSmsProvider>();
            return;
        }

        if (!realProviderRegistered)
        {
            throw new InvalidOperationException(
                "At least one real SMS provider must be enabled outside Development/Testing. " +
                "Loopback dispatch is forbidden in production-like environments.");
        }
    }

    // Feature handlers — plain classes resolved per request. Rather than list every one (a file that
    // grew with each feature and was easy to forget), scan the application assembly with Scrutor and
    // register each concrete *Handler as scoped, as itself (controllers inject the concrete type).
    // The naming convention is the contract: a new `Features/**/*Handler.cs` is wired automatically.
    // See ADR-017.
    private static IServiceCollection AddFeatureHandlers(this IServiceCollection services)
    {
        // API-key authentication — a shared resolver service, not a use-case handler, so it is
        // registered explicitly. The enforcing middleware (ApiKeyAuthenticationMiddleware) is
        // intentionally NOT added to the pipeline yet; the APIs stay open for testing. See ADR-015.
        services.AddScoped<ApiKeyAuthenticator>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.Scan(scan => scan
            .FromAssemblyOf<SendMessagesHandler>()
            .AddClasses(classes => classes.Where(type => type.Name.EndsWith("Handler")), publicOnly: true)
            .AsSelf()
            .WithScopedLifetime());

        return services;
    }
}
