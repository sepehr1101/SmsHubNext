using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace SmsHubNext.Features.Administration.Ui.Components;

public static class PersianMudLocalizationExtensions
{
    public static IServiceCollection AddPersianMudLocalization(this IServiceCollection services)
    {
        services.AddTransient<MudLocalizer, PersianMudLocalizer>();
        // Management UI is Persian even if the server or browser culture is English.
        // Keep culture-independent API parsing and numeric/date semantics unchanged.
        services.AddLocalizationInterceptor<ILocalizationInterceptor>(provider =>
            new DefaultLocalizationInterceptor(provider.GetRequiredService<ILoggerFactory>(), provider.GetRequiredService<MudLocalizer>())
            {
                IgnoreDefaultEnglish = true,
            });
        return services;
    }
}
