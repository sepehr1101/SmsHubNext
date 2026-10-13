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
    [Fact]
    public async Task Shared_grid_renders_typed_columns_and_persian_controls()
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
            });
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page =
                await renderer.RenderComponentAsync<AdminGrid<GridRow>>(parameters);
            string html = WebUtility.HtmlDecode(page.ToHtmlString());
            Assert.Contains("شرکت آب", html);
            Assert.Contains("جست‌وجو در جدول", html);
            Assert.Contains("ستون‌ها", html);
            Assert.Contains("خروجی Excel", html);
            Assert.Contains("تعداد در صفحه", html);
        });
    }

    public sealed record GridRow(string Name);
    private sealed class NoScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
