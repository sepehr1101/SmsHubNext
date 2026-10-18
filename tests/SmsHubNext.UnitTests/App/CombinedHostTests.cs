using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmsHubNext.Extensions;
using Serilog;
using Xunit;

namespace SmsHubNext.UnitTests.App;

/// <summary>Exercises real routing and authentication without migrations or background dispatch.</summary>
public sealed class CombinedHostTests
{
    [Fact]
    public async Task Single_host_serves_ui_assets_and_keeps_cookie_sessions_out_of_management_api()
    {
        string contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SmsHubNext"));
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
            ContentRootPath = contentRoot,
        });
        builder.Configuration["ConnectionStrings:SmsHubNext"] = "Server=unused;Database=unused";
        builder.Configuration["BearerTokens:Key"] = new string('k', 64);
        builder.Configuration["Providers:Magfa:Enabled"] = "false";
        builder.Configuration["Providers:Kavenegar:Enabled"] = "false";
        builder.Configuration["Providers:SmsIr:Enabled"] = "false";
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Host.UseSerilog((context, configuration) => configuration.MinimumLevel.Error());
        builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);
        builder.Services.RemoveAll<IHostedService>();
        await using WebApplication app = builder.Build();
        app.ConfigurePipeline();
        // A test-only way to obtain the same antiforgery token emitted by the interactive login form.
        app.MapGet("/test-login-token", (HttpContext context, IAntiforgery antiforgery) =>
            antiforgery.GetAndStoreTokens(context).RequestToken).AllowAnonymous();
        await app.StartAsync();

        string address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        using HttpClientHandler handler = new() { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using HttpClient client = new(handler) { BaseAddress = new Uri(address) };

        using HttpResponseMessage home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        string html = await home.Content.ReadAsStringAsync();
        Assert.Contains("_framework/blazor.web.js", html);
        string mudCssPath = Regex.Match(html, "href=\"([^\"]*_content/MudBlazor/[^\"]+\\.css)\"").Groups[1].Value;
        Assert.NotEmpty(mudCssPath);
        using HttpResponseMessage mudCss = await client.GetAsync(mudCssPath);
        Assert.Equal(HttpStatusCode.OK, mudCss.StatusCode);

        using HttpResponseMessage css = await client.GetAsync("/app.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Contains("#blazor-error-ui", await css.Content.ReadAsStringAsync());

        using HttpResponseMessage unauthenticated = await client.GetAsync("/admin/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, unauthenticated.StatusCode);
        Assert.Contains("/login", unauthenticated.Headers.Location!.ToString());
        using HttpResponseMessage monitor = await client.GetAsync("/admin/dispatch-live");
        Assert.Equal(HttpStatusCode.Redirect, monitor.StatusCode);
        Assert.Contains("/login", monitor.Headers.Location!.ToString());

        using FormUrlEncodedContent invalidForm = new(new Dictionary<string, string>
        {
            ["username"] = "Admin", ["password"] = "123456",
        });
        using HttpResponseMessage missingAntiforgery = await client.PostAsync("/auth/login", invalidForm);
        Assert.False(missingAntiforgery.IsSuccessStatusCode);
        Assert.DoesNotContain(missingAntiforgery.Headers, header => header.Key == "Set-Cookie" &&
            header.Value.Any(value => value.Contains("SmsHubNext.Development.Auth", StringComparison.Ordinal)));

        string token = (await client.GetStringAsync("/test-login-token")).Trim('"');
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["username"] = "Admin", ["password"] = "123456", ["__RequestVerificationToken"] = token,
        });
        using HttpResponseMessage login = await client.PostAsync("/auth/login", form);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/admin/dashboard", login.Headers.Location!.ToString());

        using HttpResponseMessage dashboard = await client.GetAsync("/admin/dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        using HttpResponseMessage customersPage = await client.GetAsync("/admin/customers");
        Assert.Equal(HttpStatusCode.OK, customersPage.StatusCode);
        using HttpResponseMessage livePage = await client.GetAsync("/admin/dispatch-live");
        Assert.Equal(HttpStatusCode.OK, livePage.StatusCode);
        // The host deliberately disables prerendering; interactive content arrives on the circuit.
        Assert.Contains("blazor.web.js", await livePage.Content.ReadAsStringAsync());

        using HttpResponseMessage api = await client.GetAsync("/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Contains("auth.management_token_required", await api.Content.ReadAsStringAsync());

        using HttpResponseMessage send = await client.PostAsJsonAsync("/messages", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, send.StatusCode);
        using HttpResponseMessage resend = await client.PostAsJsonAsync("/messages/1/resend", new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, resend.StatusCode);
        Assert.Contains("auth.missing_key", await resend.Content.ReadAsStringAsync());

        using HttpResponseMessage findMessages = await client.GetAsync("/messages?billId=123&fromJalali=1405/07/01&toJalali=1405/07/30");
        Assert.Equal(HttpStatusCode.Unauthorized, findMessages.StatusCode);
        Assert.Contains("auth.missing_key", await findMessages.Content.ReadAsStringAsync());

        using HttpResponseMessage health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using HttpResponseMessage openApi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
    }
}
