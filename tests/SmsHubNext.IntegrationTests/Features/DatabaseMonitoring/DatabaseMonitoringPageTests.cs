using System.Net;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using SmsHubNext.Features.Administration.Ui.Components;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Authentication.Ui;
using SmsHubNext.Features.DatabaseMonitoring;
using SmsHubNext.Features.DatabaseMonitoring.Ui;
using SmsHubNext.Features.DatabaseMonitoring.Ui.Pages;
using SmsHubNext.Shared.Security;
using SmsHubNext.Shared.Ui;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.DatabaseMonitoring;

public sealed class DatabaseMonitoringPageTests(DatabaseMonitoringFixture fixture) : IClassFixture<DatabaseMonitoringFixture>
{
    [Fact]
    public async Task Every_page_renders_through_the_authenticated_boundary_with_real_database_results()
    {
        const string key = "monitor-ui-review-test-only";
        await using SqlConnection connection = await fixture.Db.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            INSERT dbo.Customer (Name,Code) VALUES (N'آزمون رابط پایش','monitor-ui-review');
            DECLARE @CustomerId SMALLINT=SCOPE_IDENTITY();
            INSERT dbo.ApiKey (CustomerId,Name,KeyPrefix,KeyHash,AccessLevel) VALUES (@CustomerId,N'UI review','monitor',@Hash,1);
            """,new { Hash=ApiKeyHasher.HashBytes(key) });
        await new DatabaseMonitoringHandler(fixture.Db,TimeProvider.System,new DatabaseMonitoringCollectorState()).Collect(default);
        foreach (DatabaseMonitorEntry entry in DatabaseMonitoringNavigation.Pages)
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMudServices();
            services.AddPersianMudLocalization();
            services.AddSingleton<IJSRuntime, NoScriptRuntime>();
            services.AddSingleton<NavigationManager>(new TestNavigation(entry.Route));
            services.AddSingleton<AuthenticationStateProvider, AdminState>();
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext=new DefaultHttpContext() });
            services.AddSingleton(fixture.Db);
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.Configure<ManagementUiOptions>(options=>options.AdminApiKey=key);
            services.AddScoped<ApiKeyAuthenticator>();
            services.AddScoped<UiOperations>();
            services.AddSingleton<DatabaseMonitoringCollectorState>();
            services.AddScoped<DatabaseMonitoringHandler>();
            services.AddScoped<DatabaseMonitoringService>();
            await using ServiceProvider provider=services.BuildServiceProvider();
            await using HtmlRenderer renderer=new(provider,provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<MudPopoverProvider>();
                Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page=await renderer.RenderComponentAsync<DatabaseMonitorPage>(ParameterView.FromDictionary(new Dictionary<string,object?> { ["Section"]=entry.Route }));
                string html=WebUtility.HtmlDecode(page.ToHtmlString());
                Assert.DoesNotContain("_error",html);
                Assert.DoesNotContain(">N",html);
                Assert.DoesNotContain("role=\"alert\"",html);
                Assert.Contains(entry.Note,html);
                if(entry.Section is not DatabaseMonitorSection.Indexes)
                    Assert.Contains("آخرین بررسی",html);
                if(entry.Section is DatabaseMonitorSection.Overview) Assert.Contains("MonitorTest",html);
                if(entry.Section is DatabaseMonitorSection.Tables) Assert.Contains("monitor-chart",html);
                if(entry.Section is DatabaseMonitorSection.Queries) Assert.Contains("رتبه‌بندی بر اساس",html);
                Assert.DoesNotContain("disabled=\"_loading\"",html);
                await WritePreview(renderer,entry,page.ToHtmlString());
            });
        }
    }
    private static async Task WritePreview(HtmlRenderer renderer,DatabaseMonitorEntry entry,string content)
    {
        string? output=Environment.GetEnvironmentVariable("SMSHUB_MONITOR_PREVIEW");
        if(string.IsNullOrWhiteSpace(output)) return;
        DirectoryInfo? root=new(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"SmsHubNext.slnx"))) root=root.Parent;
        Assert.NotNull(root);
        Directory.CreateDirectory(output);
        string mudCss=Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? "D:/.nuget/packages","mudblazor/9.11.0/staticwebassets/MudBlazor.min.css");
        File.Copy(mudCss,Path.Combine(output,"MudBlazor.min.css"),true);
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent sidebar=await renderer.RenderComponentAsync<AdminSidebar>();
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent breadcrumb=await renderer.RenderComponentAsync<AdminBreadcrumb>();
        string document=$"""
            <!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <link rel="stylesheet" href="MudBlazor.min.css"><link rel="stylesheet" href="/src/SmsHubNext/wwwroot/app.css"><link rel="stylesheet" href="/src/SmsHubNext/wwwroot/admin.css"><link rel="stylesheet" href="/src/SmsHubNext/obj/Release/net10.0/scopedcss/bundle/SmsHubNext.styles.css">
            <title>{entry.Title}</title></head><body><div class="admin-shell">{sidebar.ToHtmlString()}<div class="admin-main"><main class="admin-content">{breadcrumb.ToHtmlString()}{content}</main></div></div></body></html>
            """;
        await File.WriteAllTextAsync(Path.Combine(output,entry.Route+".html"),document);
    }
    private sealed class AdminState : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role,DevelopmentAccount.AdministratorRole)],DevelopmentAccount.AuthenticationScheme))));
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation(string route)=>Initialize("http://localhost/","http://localhost/admin/settings/database/"+route);
        protected override void NavigateToCore(string uri,bool forceLoad) { }
    }
    private sealed class NoScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args)=>ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken token,object?[]? args)=>ValueTask.FromResult(default(TValue)!);
    }
}

