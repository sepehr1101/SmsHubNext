using MudBlazor.Services;
using SmsHubNext.App.Features.Authentication;
using SmsHubNext.App.Features.Customers;
using SmsHubNext.App.Features.MessageTypes;
using SmsHubNext.App.Features.Providers;
using SmsHubNext.App.Features.ProviderAccounts;
using SmsHubNext.App.Features.SenderLines;
using SmsHubNext.App.Features.GeoSections;
using SmsHubNext.App.Features.Tariffs;
using SmsHubNext.App.Features.ApiKeys;
using SmsHubNext.App.Features.Billing;
using SmsHubNext.App.Features.DispatchOperations;
using SmsHubNext.App.Features.Reports;
using SmsHubNext.App.Features.Inbound;
using SmsHubNext.App.Features.DeliveryReports;
using SmsHubNext.App.Features.Sending;
using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddMudServices();
        services.AddCascadingAuthenticationState();
        services.AddSingleton(TimeProvider.System);
        services.Configure<SmsHubApiOptions>(configuration.GetSection(SmsHubApiOptions.SectionName));
        services.AddHttpClient();
        services.AddScoped<ManagementApiClient>();
        services.AddSingleton<ManagementAccessTokenProvider>();
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

        services.AddAuthentication(DevelopmentAccount.AuthenticationScheme)
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
        services.AddAuthorization();

        return services;
    }
}
