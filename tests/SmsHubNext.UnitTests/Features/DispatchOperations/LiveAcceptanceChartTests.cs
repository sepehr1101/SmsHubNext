using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsHubNext.Features.DispatchOperations;
using SmsHubNext.Features.DispatchOperations.Ui.Pages;
using Xunit;

namespace SmsHubNext.UnitTests.Features.DispatchOperations;

public sealed class LiveAcceptanceChartTests
{
    [Fact]
    public async Task Disconnect_keeps_last_counts_and_reconnection_clears_warning()
    {
        ServiceCollection services = new();
        services.AddLogging();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        DateTime end = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        LiveSnapshot snapshot = new(end, end.AddHours(-24), new(0, 0, 7, 0, 0, 0, 0),
            LiveDispatchBuckets.Fill(end, [new(end.AddSeconds(-10), 7)]));
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent chart = await renderer.RenderComponentAsync<LiveAcceptanceChart>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Snapshot"] = snapshot, ["Disconnected"] = true }));
            string disconnected = System.Net.WebUtility.HtmlDecode(chart.ToHtmlString());
            Assert.Contains("صفر محسوب نمی‌شود", disconnected);
            Assert.Contains("۷", disconnected);
            Assert.Equal(60, disconnected.Split("class=\"live-bar-slot\"").Length - 1);
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent connected = await renderer.RenderComponentAsync<LiveAcceptanceChart>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Snapshot"] = snapshot, ["Disconnected"] = false }));
            string reconnected = System.Net.WebUtility.HtmlDecode(connected.ToHtmlString());
            Assert.DoesNotContain("صفر محسوب نمی‌شود", reconnected);
            Assert.Contains("۷", reconnected);
        });
    }
}
