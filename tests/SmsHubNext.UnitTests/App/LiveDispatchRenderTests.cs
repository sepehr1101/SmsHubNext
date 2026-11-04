using System.Net;
using System.Reflection;
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
using SmsHubNext.Features.DispatchOperations;
using SmsHubNext.Features.DispatchOperations.Ui;
using SmsHubNext.Features.DispatchOperations.Ui.Pages;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Ui;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class LiveDispatchRenderTests
{
    public sealed class DeliveredWithBatchHistory : LiveDispatch
    {
        protected override Task OnInitializedAsync()
        {
            DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
            LiveMessage message = new(42, "1405/08/10", 9, "989120000001", "40401403", "4040",
                SmsHubNext.Shared.Enums.SendStatus.Submitted, SmsHubNext.Shared.Enums.DeliveryStatus.Delivered,
                now, now, 3, "پیام ساختگی", now.AddMinutes(-2), null, SmsHubNext.Shared.Enums.MessageBatchEventType.AwaitingConfirmation);
            typeof(LiveDispatch).GetField("_messages", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(this, new LiveMessagePage([message], false, now, null));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Live_page_has_one_search_global_filter_action_and_paging_at_both_ends()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddOptions();
        services.AddMudServices();
        services.AddHttpContextAccessor();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IJSRuntime, NoScriptRuntime>();
        services.AddSingleton<AuthenticationStateProvider, AnonymousState>();
        services.AddSingleton(new Db("unused"));
        services.AddTransient<ApiKeyAuthenticator>();
        services.AddTransient<UiOperations>();
        services.AddSingleton<LiveDispatchCache>();
        services.AddTransient<LiveDispatchHandler>();
        services.AddTransient<LiveDispatchService>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page = await renderer.RenderComponentAsync<LiveDispatch>();
            string html = WebUtility.HtmlDecode(page.ToHtmlString());
            Assert.Single(Regex.Matches(html, "type=\"search\""));
            Assert.Equal(2, Regex.Matches(html, "aria-label=\"صفحه‌بندی پیام‌ها\"").Count);
            Assert.Contains("aria-label=\"فیلترها\"", html);
            Assert.Contains("aria-label=\"خروجی اکسل\"", html);
            Assert.Contains("aria-label=\"نمایش ستون‌ها\"", html);
            Assert.DoesNotContain("تلاش‌ها در سطح دسته ثبت می‌شوند", html);

            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent delivered = await renderer.RenderComponentAsync<DeliveredWithBatchHistory>();
            string deliveredHtml = WebUtility.HtmlDecode(delivered.ToHtmlString());
            Assert.Contains("تحویل‌شده", deliveredHtml);
            Assert.Contains("سابقه خطای دسته", deliveredHtml);
            Assert.Contains("سابقه پاسخ نامشخص در دسته", deliveredHtml);
            Assert.DoesNotContain("پاسخ نامشخص؛ انتظار تأیید", deliveredHtml);
        });
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
