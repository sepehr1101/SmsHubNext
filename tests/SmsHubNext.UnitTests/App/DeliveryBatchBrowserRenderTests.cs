using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.DeliveryReports;
using SmsHubNext.Features.DeliveryReports.Ui;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Ui;
using Xunit;
using DeliveryPage = SmsHubNext.Features.DeliveryReports.Ui.Pages.DeliveryReports;

namespace SmsHubNext.UnitTests.App;

public sealed class DeliveryBatchBrowserRenderTests
{
    [Fact]
    public async Task Shows_date_time_range_and_server_page_controls_instead_of_batch_number_input()
    {
        await using ServiceProvider provider = BuildServices();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page = await renderer.RenderComponentAsync<DeliveryPage>();
            string html = WebUtility.HtmlDecode(page.ToHtmlString());
            Assert.Contains("از تاریخ شمسی", html);
            Assert.Contains("تا تاریخ شمسی", html);
            Assert.Contains("از ساعت (تهران)", html);
            Assert.Contains("تا ساعت (تهران)", html);
            Assert.Equal(2, Regex.Matches(html, "type=\"time\"").Count);
            Assert.DoesNotContain("شناسه دسته ارسال", html);
            Assert.DoesNotContain("type=\"number\"", html);
            Assert.Contains("نمایش دسته‌ها", html);
            Assert.Contains("صفحه قبل", html);
            Assert.Contains("صفحه بعد", html);
        });
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Resend_panel_exposes_action_only_when_eligible_and_shows_cost_warning(bool allowed, bool bodyAvailable)
    {
        await using ServiceProvider provider = BuildServices();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent panel =
                await renderer.RenderComponentAsync<SmsHubNext.Features.DeliveryReports.Ui.Pages.ResendMessagePanel>(
                    Microsoft.AspNetCore.Components.ParameterView.FromDictionary(new Dictionary<string, object?>
                    {
                        ["MessageId"] = 42L, ["Recipient"] = "989120000001", ["Allowed"] = allowed, ["BodyAvailable"] = bodyAvailable,
                    }));
            string html = WebUtility.HtmlDecode(panel.ToHtmlString());
            Assert.Contains("هزینه جدید", html);
            Assert.Equal(allowed && bodyAvailable, html.Contains("ارسال مجدد این پیامک", StringComparison.Ordinal));
            if (!bodyAvailable) Assert.Contains("متن پیام دیگر نگهداری نمی‌شود", html);
            Assert.DoesNotContain("تأیید و ثبت در صف", html);
        });
    }

    private static ServiceProvider BuildServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddOptions();
        services.AddMudServices();
        services.AddHttpContextAccessor();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IJSRuntime, NoScriptRuntime>();
        services.AddSingleton<AuthenticationStateProvider, AnonymousState>();
        services.AddSingleton(new Db("Server=unused;Database=unused"));
        services.AddTransient<ApiKeyAuthenticator>();
        services.AddTransient<UiOperations>();
        services.AddTransient<BrowseBatchesHandler>();
        services.AddTransient<ListBatchMessagesHandler>();
        services.AddTransient<ListDeliveryReportsHandler>();
        services.AddTransient<IngestDeliveryReportHandler>();
        services.AddTransient<DeliveryReportsService>();
        services.AddTransient<SmsHubNext.Features.Sending.SendMessagesHandler>();
        services.AddTransient<SmsHubNext.Features.Sending.ResendMessageHandler>();
        return services.BuildServiceProvider();
    }

    private sealed class AnonymousState : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class NoScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}

