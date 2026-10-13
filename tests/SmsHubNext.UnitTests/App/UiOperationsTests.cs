using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Authentication.Ui;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Ui;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class UiOperationsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("Bearer", "Administrator")]
    [InlineData(DevelopmentAccount.AuthenticationScheme, "Customer")]
    public async Task Direct_ui_calls_require_an_administrator_cookie_session(string? scheme, string? role)
    {
        ClaimsPrincipal principal = Principal(scheme, role);
        UiOperations operations = CreateOperations(principal);
        bool invoked = false;

        UiResult<int> result = await operations.Run<int>((identity, token) =>
        {
            invoked = true;
            return Task.FromResult(Result.Success(1));
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(invoked);
        Assert.Contains("وارد پنل", result.ErrorMessage);
    }

    [Theory]
    [InlineData(false, "ManagementUi:AdminApiKey")]
    [InlineData(true, "ManagementUi:CustomerApiKey")]
    public async Task Missing_key_is_a_page_failure_without_attempting_a_database_call(bool customerKey, string setting)
    {
        UiOperations operations = CreateOperations(Principal(DevelopmentAccount.AuthenticationScheme, "Administrator"));
        bool invoked = false;

        UiResult<int> result = await operations.Run<int>((identity, token) =>
        {
            invoked = true;
            return Task.FromResult(Result.Success(1));
        }, CancellationToken.None, customerKey);

        Assert.False(result.IsSuccess);
        Assert.False(invoked);
        Assert.Contains(setting, result.ErrorMessage);
    }

    [Fact]
    public void Management_pages_do_not_collide_with_external_api_routes()
    {
        List<string> routes = typeof(Program).Assembly.GetTypes()
            .Where(type => type.Namespace?.Contains(".Ui.Pages", StringComparison.Ordinal) == true &&
                type.Namespace != "SmsHubNext.Features.Authentication.Ui.Pages")
            .SelectMany(type => type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), false)
                .Cast<Microsoft.AspNetCore.Components.RouteAttribute>())
            .Select(attribute => attribute.Template)
            .ToList();

        Assert.True(routes.Count >= 15);
        Assert.All(routes, route => Assert.StartsWith("/admin/", route));
        Assert.Equal(routes.Count, routes.Distinct().Count());
    }

    private static UiOperations CreateOperations(ClaimsPrincipal principal) => new(
        new FixedAuthenticationStateProvider(principal),
        new ApiKeyAuthenticator(new Db("Server=unused;Database=unused"), TimeProvider.System),
        Options.Create(new ManagementUiOptions()),
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
        NullLogger<UiOperations>.Instance);

    private static ClaimsPrincipal Principal(string? scheme, string? role)
    {
        Claim[] claims = role is null ? [] : [new Claim(ClaimTypes.Role, role)];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }
}
