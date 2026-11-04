using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using SmsHubNext.Features.Reports;
using SmsHubNext.Features.Reports.Ui.Pages;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class DimensionReportRenderTests
{
    [Theory]
    [InlineData(false, "۲")]
    [InlineData(true, "۱۲٫۵")]
    public async Task Matrix_pivots_types_and_renders_missing_cells_as_zero(bool cost, string expected)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMudServices();
        services.AddSingleton<IJSRuntime, NoScriptRuntime>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page = await renderer.RenderComponentAsync<DimensionMatrix>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Cost"] = cost,
                ["Rows"] = new DimensionReportRow[]
                {
                    new() { GeoSectionId = 1, GeoName = "تهران", MessageTypeId = 1, MessageTypeName = "قبض", MessageCount = 2, TotalCost = 12.5m },
                    new() { GeoSectionId = 2, GeoName = "فارس", MessageTypeId = 2, MessageTypeName = "اخطار", MessageCount = 3, TotalCost = 20m },
                },
            }));
            string html = WebUtility.HtmlDecode(page.ToHtmlString());
            Assert.Contains("تهران", html);
            Assert.Contains("قبض", html);
            Assert.Contains("اخطار", html);
            Assert.Contains(expected, html);
            Assert.Contains("۰", html);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "type=\"search\""));
        });
    }
    [Fact]
    public async Task Trend_includes_months_without_messages()
    {
        ServiceCollection services = new();
        services.AddLogging();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page = await renderer.RenderComponentAsync<DimensionTrend>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["From"] = "1404/12/01", ["To"] = "1405/02/31", ["Geography"] = true,
                ["Rows"] = new DimensionReportRow[] { new() { GeoSectionId = 1, GeoName = "تهران", JalaliMonth = "1405/02", MessageCount = 5 } },
            }));
            string html = WebUtility.HtmlDecode(page.ToHtmlString());
            Assert.Contains("۱۴۰۴/۱۲", html);
            Assert.Contains("۱۴۰۵/۰۱", html);
            Assert.Contains("۱۴۰۵/۰۲", html);
            Assert.Contains("height:0%", html);
            Assert.Contains("height:100%", html);
        });
    }
    private sealed class NoScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}

