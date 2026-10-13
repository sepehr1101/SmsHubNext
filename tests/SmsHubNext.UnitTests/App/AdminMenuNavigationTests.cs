using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsHubNext.Features.Administration.Ui.Pages;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public class AdminMenuNavigationTests
{
    [Fact]
    public async Task MenuHub_UpdatesCardsWhenNavigatingWithoutReload()
    {
        TestNavigation navigation = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(navigation);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page =
                await renderer.RenderComponentAsync<MenuHub>();
            Assert.Contains("/admin/settings/general", page.ToHtmlString());

            navigation.Go("admin/operations");
            Assert.Contains("/admin/send", page.ToHtmlString());
            Assert.DoesNotContain("/admin/settings/general", page.ToHtmlString());

            navigation.Go("admin/report-center");
            Assert.Contains("/admin/reports", page.ToHtmlString());
            Assert.DoesNotContain("/admin/send", page.ToHtmlString());

            navigation.Go("admin/settings");
            Assert.Contains("/admin/settings/general", page.ToHtmlString());
            Assert.DoesNotContain("/admin/reports", page.ToHtmlString());
        });
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/settings");
        public void Go(string route) => NavigateTo(route);
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }
}
