using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsHubNext.Features.DispatchOperations.Ui.Pages;
using Xunit;

namespace SmsHubNext.UnitTests.Features.DispatchOperations;

public sealed class LiveMessagePagerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pager_exposes_persian_page_sizes_and_navigation_beyond_the_old_page_cap(bool loading)
    {
        ServiceCollection services = new();
        services.AddLogging();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent pager =
                await renderer.RenderComponentAsync<LiveMessagePager>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Page"] = 5001, ["PageSize"] = 100, ["Count"] = 100, ["HasMore"] = true, ["Loading"] = loading,
                }));
            string html = WebUtility.HtmlDecode(pager.ToHtmlString());
            Assert.Contains("صفحه ۵۰۰۱", html);
            Assert.Contains("۵۰۰۰۰۱ تا ۵۰۰۱۰۰", html);
            Assert.Contains(">۲۵</option>", html);
            Assert.Contains(">۵۰</option>", html);
            Assert.Contains(">۱۰۰</option>", html);
            Assert.Contains("aria-label=\"صفحه قبل\"", html);
            Assert.Contains("aria-label=\"صفحه بعد\"", html);
            Assert.Equal(loading, html.Contains(" disabled", StringComparison.Ordinal));
        });
    }
}
