using System.Linq.Expressions;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using SmsHubNext.Features.Administration.Ui.Components;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class AdminGridRenderTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Shared_grid_renders_one_search_and_persian_icon_controls(bool pageLocal, bool showSearch)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMudServices();
        services.AddSingleton<IJSRuntime, NoScriptRuntime>();
        services.AddTransient<MudLocalizer, PersianMudLocalizer>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            RenderFragment columns = builder =>
            {
                builder.OpenComponent<PropertyColumn<GridRow, string>>(0);
                builder.AddAttribute(1, "Title", "مشتری");
                builder.AddAttribute(2, "Property", (Expression<Func<GridRow, string>>)(row => row.Name));
                builder.CloseComponent();
            };
            ParameterView parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Items"] = new GridRow[] { new("شرکت آب") },
                ["ExportColumns"] = new AdminExportColumn<GridRow>[] { new("مشتری", row => row.Name) },
                ["ChildContent"] = columns,
                ["PageLocal"] = pageLocal,
                ["ShowSearch"] = showSearch,
                ["OnAdd"] = EventCallback.Factory.Create(new object(), () => { }),
                ["AddLabel"] = "مشتری جدید",
            });
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page =
                await renderer.RenderComponentAsync<AdminGrid<GridRow>>(parameters);
            string html = WebUtility.HtmlDecode(page.ToHtmlString());
            Assert.Contains("شرکت آب", html);
            Assert.Equal(showSearch, html.Contains("جست‌وجو در جدول", StringComparison.Ordinal));
            Assert.Contains("ستون‌ها", html);
            Assert.Contains("aria-label=\"خروجی اکسل\"", html);
            Assert.Contains("aria-label=\"فیلترها\"", html);
            Assert.Contains("aria-label=\"نمایش ستون‌ها\"", html);
            Assert.Contains("aria-label=\"مشتری جدید\"", html);
            Assert.Contains("admin-grid-icon excel", html);
            Assert.Equal(showSearch ? 1 : 0, System.Text.RegularExpressions.Regex.Matches(html, "type=\"search\"").Count);
            Assert.DoesNotContain("جست‌وجو و مرتب‌سازی در صفحه بارگذاری‌شده", html);
            if (!pageLocal)
            {
                Assert.Contains("تعداد در صفحه", html);
                Assert.Contains("۱ تا ۱ از ۱", html);
                Assert.Contains(">۲۵</option>", html);
            }
        });
    }

    public sealed record GridRow(string Name);
    private sealed class NoScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
