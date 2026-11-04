using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using SmsHubNext.Extensions;
using SmsHubNext.Features.Administration;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Logging;
using SmsHubNext.Shared.Errors;
using SmsHubNext.Shared.Http;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class HttpErrorLoggingTests
{
    [Fact]
    public async Task Reader_filters_tehran_dates_and_daily_hours_before_limiting_results()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SmsHubNext-http-logs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using StreamWriter writer = new(Path.Combine(directory, "http-errors-test.jsonl"));
            DateTimeOffset start = new(2026, 11, 1, 4, 30, 0, TimeSpan.Zero); // 08:00 Tehran.
            for (int index = 0; index < 1002; index++)
                await writer.WriteLineAsync(Line(start.AddDays(1).AddSeconds(index), 500));
            await writer.WriteLineAsync(Line(start.AddTicks(-1), 400));
            await writer.WriteLineAsync(Line(start, 400));
            await writer.WriteLineAsync(Line(start.AddDays(1).AddHours(1), 401));
            await writer.WriteLineAsync(Line(start.AddDays(1).AddHours(1).AddTicks(1), 404));
            await writer.WriteLineAsync(Line(start.AddDays(2), 429));
            await writer.WriteLineAsync(Line(start.AddHours(-6), 400));
            await writer.WriteLineAsync(Line(start.AddHours(14), 401));
            await writer.WriteLineAsync("{broken");
            await writer.FlushAsync();
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            ListHttpErrorsHandler handler = new(Options.Create(new ApplicationLogOptions { DirectoryPath = directory }), builder.Environment);
            SmsHubNext.Shared.Results.Result<HttpErrorResults> result = await handler.Handle(new HttpErrorQuery
            {
                StatusClass = "4XX", FromDate = new DateOnly(2026, 11, 1), ToDate = new DateOnly(2026, 11, 2),
                FromTime = new TimeOnly(8, 0), ToTime = new TimeOnly(9, 0),
            }, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { 401, 400 }, result.Value.Items.Select(item => item.StatusCode));
            Assert.False(result.Value.Truncated);
            Assert.Equal(1, result.Value.SkippedLines);
            result = await handler.Handle(new HttpErrorQuery
            {
                StatusClass = "5xx", FromDate = new DateOnly(2026, 11, 1), ToDate = new DateOnly(2026, 11, 2),
            }, CancellationToken.None);
            Assert.True(result.Value.Truncated);
            Assert.Equal(1000, result.Value.Items.Count);
            Assert.Equal(start.AddDays(1).AddSeconds(1001), result.Value.Items[0].Timestamp);
            Assert.Equal(start.AddDays(1).AddSeconds(2), result.Value.Items[^1].Timestamp);
            result = await handler.Handle(new HttpErrorQuery
            {
                StatusClass = "4xx", FromDate = new DateOnly(2026, 11, 1), ToDate = new DateOnly(2026, 11, 2),
                FromTime = new TimeOnly(22, 0), ToTime = new TimeOnly(2, 0),
            }, CancellationToken.None);
            Assert.Equal(new[] { 401, 400 }, result.Value.Items.Select(item => item.StatusCode));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("3xx", 0, false)]
    [InlineData("4xx", 31, false)]
    [InlineData("4xx", -1, false)]
    [InlineData("5xx", 30, true)]
    public void Query_requires_supported_group_and_at_most_31_inclusive_days(string group, int days, bool valid)
    {
        DateOnly start = new(2026, 11, 1);
        Assert.Equal(valid, new HttpErrorQuery { StatusClass = group, FromDate = start, ToDate = start.AddDays(days) }.Validate().IsSuccess);
        Assert.True(new HttpErrorQuery { StatusClass = "4xx" }.Validate().IsFailure);
    }

    private static string Line(DateTimeOffset timestamp, int code) => System.Text.Json.JsonSerializer.Serialize(new
    {
        Timestamp = timestamp,
        Properties = new { StatusCode = code, RequestMethod = "GET", RequestPath = "/synthetic", TraceId = "test-trace" },
    });

    [Fact]
    public async Task Real_http_responses_are_logged_and_filtered_through_administrator_api()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SmsHubNext-http-logs", Guid.NewGuid().ToString("N"));
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApplicationLogs:DirectoryPath"] = directory,
            ["Serilog:MinimumLevel:Default"] = "Error",
        });
        builder.AddApplicationLogging();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ListHttpErrorsHandler>();
        builder.Services.AddControllers().AddApplicationPart(typeof(LoggingController).Assembly);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(AuthenticationPolicies.Administrator,
            policy => policy.RequireAuthenticatedUser().RequireRole(AuthenticationPolicies.AdministratorRole)));
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        await using (WebApplication app = builder.Build())
        {
            app.UseSerilogRequestLogging(LoggingExtensions.ConfigureHttpRequestLogging);
            app.UseExceptionHandler();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            app.MapGet("/synthetic/{status:int}", (int status) => Results.StatusCode(status));
            app.MapGet("/timeout", (Func<IResult>)(() => throw new TimeoutException("synthetic timeout")));
            await app.StartAsync();
            using HttpClient client = new(new HttpClientHandler { UseProxy = false })
            {
                BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10),
            };

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/logging/errors")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/logging/errors")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");
            foreach (int status in new[] { 200, 400, 401, 404, 429, 500 })
                Assert.Equal(status, (int)(await client.GetAsync($"/synthetic/{status}?secret=must-not-be-logged")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/timeout")).StatusCode);

            DateOnly today = DateOnly.FromDateTime(ApplicationLogTime.ToTehran(TimeProvider.System.GetUtcNow()));
            string dates = $"fromDate={today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&toDate={today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            ApiResponse<HttpErrorResults>? errors = await client.GetFromJsonAsync<ApiResponse<HttpErrorResults>>($"/logging/errors?statusClass=4xx&{dates}");
            Assert.True(errors!.Success);
            Assert.Equal(new[] { 400, 401, 403, 404, 429 }, errors.Data!.Items.Select(item => item.StatusCode).Distinct().Order());
            Assert.All(errors.Data.Items, item =>
            {
                Assert.Equal("GET", item.Method);
                Assert.NotEmpty(item.TraceId);
                Assert.DoesNotContain("secret", item.Path);
            });
            errors = await client.GetFromJsonAsync<ApiResponse<HttpErrorResults>>($"/logging/errors?statusClass=5xx&{dates}");
            Assert.Equal(new[] { 500, 503 }, errors!.Data!.Items.Select(item => item.StatusCode).Order());
            Assert.Single(errors.Data.Items, item => item.Path == "/timeout");
            errors = await client.GetFromJsonAsync<ApiResponse<HttpErrorResults>>($"/logging/errors?statusClass=4xx&statusCode=404&{dates}");
            Assert.Single(errors!.Data!.Items);
            Assert.Equal(404, errors.Data.Items[0].StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/logging/errors?statusClass=3xx&{dates}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/logging/errors?statusClass=4xx&statusCode=500&{dates}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/logging/errors?statusClass=4xx&fromTime=08:00&{dates}")).StatusCode);
            await app.StopAsync();
        }
        Directory.Delete(directory, recursive: true);
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string header = Request.Headers.Authorization.ToString();
            if (header is not ("Bearer admin" or "Bearer customer")) return Task.FromResult(AuthenticateResult.NoResult());
            ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, "synthetic-user"),
                new Claim(ClaimTypes.Role, header == "Bearer admin" ? AuthenticationPolicies.AdministratorRole : "Customer")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
