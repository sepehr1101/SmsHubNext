using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsHubNext.Features.Administration.Ui.Components;
using SmsHubNext.Features.DatabaseMonitoring;
using SmsHubNext.Features.DatabaseMonitoring.Ui;
using SmsHubNext.Features.DatabaseMonitoring.Ui.Pages;
using Xunit;

namespace SmsHubNext.UnitTests.Features.DatabaseMonitoring;

public sealed class DatabaseMonitoringUiTests
{
    [Fact]
    public async Task Manual_index_page_has_no_literal_error_and_controls_are_enabled()
    {
        ServiceCollection services = new();
        services.AddLogging();
        // No operation should be invoked before the administrator starts the manual scan.
        services.AddSingleton(new DatabaseMonitoringService(null!,null!));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page = await renderer.RenderComponentAsync<DatabaseMonitorPage>(ParameterView.FromDictionary(new Dictionary<string,object?> { ["Section"]="indexes" }));
            string html = WebUtility.HtmlDecode(page.ToHtmlString());
            Assert.DoesNotContain("_error",html);
            Assert.DoesNotContain("role=\"alert\"",html);
            Assert.DoesNotContain("disabled",html);
            Assert.Contains("بررسی سبک ایندکس‌ها",html);
        });
    }

    [Fact]
    public async Task Sidebar_exposes_monitoring_as_a_top_level_link()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new TestNavigation());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent sidebar = await renderer.RenderComponentAsync<AdminSidebar>();
            string html = WebUtility.HtmlDecode(sidebar.ToHtmlString());
            Assert.Contains("href=\"/admin/settings/database\"",html);
            Assert.DoesNotContain("admin-settings-subnav",html);
            Assert.Contains("پایش پایگاه داده",html);
        });
    }

    [Fact]
    public async Task Home_renders_all_nine_cards_and_settings_breadcrumb_is_nested()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new TestNavigation());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent home = await renderer.RenderComponentAsync<DatabaseMonitorHome>();
            string html = WebUtility.HtmlDecode(home.ToHtmlString());
            foreach (DatabaseMonitorEntry entry in DatabaseMonitoringNavigation.Pages)
                Assert.Contains(DatabaseMonitoringNavigation.Root+"/"+entry.Route,html);
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent breadcrumb = await renderer.RenderComponentAsync<AdminBreadcrumb>();
            html = WebUtility.HtmlDecode(breadcrumb.ToHtmlString());

            Assert.Contains("/admin/settings/database",html);
            Assert.Contains("سلامت ایندکس‌ها",html);
        });
    }
    [Fact]
    public void Collector_error_is_explicit_and_does_not_invent_a_success_time()
    {
        DatabaseMonitoringCollectorState state = new();
        DateTime first = new(2026,10,25,0,0,0,DateTimeKind.Utc);
        state.Failed(first);
        Assert.Null(state.Status.LastSuccessUtc);
        Assert.NotNull(state.Status.Error);
        state.Succeeded(first.AddMinutes(15));
        state.Failed(first.AddMinutes(30));
        Assert.Equal(first.AddMinutes(15),state.Status.LastSuccessUtc);
        Assert.Equal(first.AddMinutes(30),state.Status.LastAttemptUtc);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/","http://localhost/admin/settings/database/indexes");
        protected override void NavigateToCore(string uri,bool forceLoad) { }
    }
}



