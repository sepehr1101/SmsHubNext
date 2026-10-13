using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using MudBlazor.Services;
using SmsHubNext.Features.Authentication.Ui;
using SmsHubNext.Features.ApiKeys.Ui;
using SmsHubNext.Features.Billing.Ui;
using SmsHubNext.Features.DeliveryReports.Ui;
using SmsHubNext.Features.DispatchOperations.Ui;
using SmsHubNext.Features.Inbound.Ui;
using SmsHubNext.Features.ProviderAccounts.Ui;
using SmsHubNext.Features.ReferenceData.Customers.Ui;
using SmsHubNext.Features.ReferenceData.GeoSections.Ui;
using SmsHubNext.Features.ReferenceData.MessageTypes.Ui;
using SmsHubNext.Features.ReferenceData.Providers.Ui;
using SmsHubNext.Features.ReferenceData.SenderLines.Ui;
using SmsHubNext.Features.Reports.Ui;
using SmsHubNext.Features.Sending.Ui;
using SmsHubNext.Features.Tariffs.Ui;
using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Extensions;

public static class ManagementUiExtensions
{
    public static IServiceCollection AddManagementUi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddMudServices();
        services.AddTransient<MudBlazor.MudLocalizer, SmsHubNext.Features.Administration.Ui.Components.PersianMudLocalizer>();
        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();
        services.Configure<ManagementUiOptions>(configuration.GetSection(ManagementUiOptions.SectionName));
        services.AddScoped<UiOperations>();
        services.AddScoped<CustomersService>();
        services.AddScoped<ProvidersService>();
        services.AddScoped<MessageTypesService>();
        services.AddScoped<ProviderAccountsService>();
        services.AddScoped<SenderLinesService>();
        services.AddScoped<GeoSectionsService>();
        services.AddScoped<TariffsService>();
        services.AddScoped<ApiKeysService>();
        services.AddScoped<BillingService>();
        services.AddScoped<DispatchOperationsService>();
        services.AddScoped<ReportsService>();
        services.AddScoped<InboundMessagesService>();
        services.AddScoped<DeliveryReportsService>();
        services.AddScoped<SendingService>();

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Application";
            options.DefaultAuthenticateScheme = "Application";
            options.DefaultChallengeScheme = "Application";
        })
            .AddPolicyScheme("Application", "UI cookies or API bearer tokens", options =>
            {
                options.ForwardDefaultSelector = context => IsUiPath(context.Request.Path)
                    ? DevelopmentAccount.AuthenticationScheme
                    : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddCookie(DevelopmentAccount.AuthenticationScheme, options =>
            {
                options.Cookie.Name = "SmsHubNext.Development.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
            });
        services.AddAuthorization(options => options.AddPolicy(DevelopmentAccount.ManagementPolicy,
            new AuthorizationPolicyBuilder(DevelopmentAccount.AuthenticationScheme)
                .RequireAuthenticatedUser().RequireRole(DevelopmentAccount.AdministratorRole).Build()));
        return services;
    }

    public static WebApplication MapManagementUi(this WebApplication app)
    {
        app.MapPost("/auth/login", Login).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/auth/logout", Logout).RequireAuthorization(DevelopmentAccount.ManagementPolicy)
            .ExcludeFromDescription();
        app.MapStaticAssets();
        app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();
        return app;
    }

    private static bool IsUiPath(PathString path) =>
        path == "/" || path == "/login" || path == "/Error" || path == "/not-found" ||
        path == "/auth/login" || path == "/auth/logout" ||
        path.StartsWithSegments("/admin") || path.StartsWithSegments("/_blazor");

    private static async Task<IResult> Login(
        HttpContext context, IAntiforgery antiforgery, IWebHostEnvironment environment, TimeProvider clock)
    {
        if (!environment.IsDevelopment())
            return Results.NotFound();
        await antiforgery.ValidateRequestAsync(context);
        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
        bool credentialsAreValid =
            string.Equals(form["username"].ToString().Trim(), DevelopmentAccount.UserName, StringComparison.OrdinalIgnoreCase) &&
            form["password"] == DevelopmentAccount.Password;
        if (!credentialsAreValid)
            return Results.Redirect("/login?error=invalid_credentials");

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, DevelopmentAccount.UserName),
            new(ClaimTypes.Name, DevelopmentAccount.UserName),
            new(ClaimTypes.Role, DevelopmentAccount.AdministratorRole),
        ];
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, DevelopmentAccount.AuthenticationScheme));
        bool rememberMe = string.Equals(form["remember"], "on", StringComparison.Ordinal);
        AuthenticationProperties properties = new()
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? clock.GetUtcNow().AddDays(7) : null,
            AllowRefresh = true,
        };
        await context.SignInAsync(DevelopmentAccount.AuthenticationScheme, principal, properties);
        return Results.Redirect("/admin/dashboard");
    }

    private static async Task<IResult> Logout(HttpContext context, IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(context);
        await context.SignOutAsync(DevelopmentAccount.AuthenticationScheme);
        return Results.Redirect("/login");
    }
}
