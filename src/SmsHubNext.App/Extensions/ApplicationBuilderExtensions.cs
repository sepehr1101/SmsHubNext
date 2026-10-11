using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using SmsHubNext.App.Components;
using SmsHubNext.App.Features.Authentication;

namespace SmsHubNext.App.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapStaticAssets();

        MapDevelopmentAuthentication(app);

        app.MapRazorComponents<SmsHubNext.App.Components.App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

    private static void MapDevelopmentAuthentication(WebApplication app)
    {
        app.MapPost("/auth/login", Login).AllowAnonymous();
        app.MapPost("/auth/logout", Logout).RequireAuthorization();
    }

    private static async Task<IResult> Login(
        HttpContext context,
        IAntiforgery antiforgery,
        IWebHostEnvironment environment,
        TimeProvider clock)
    {
        await antiforgery.ValidateRequestAsync(context);

        if (!environment.IsDevelopment())
            return Results.NotFound();

        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
        string userName = form["username"].ToString().Trim();
        string password = form["password"].ToString();

        bool credentialsAreValid =
            string.Equals(userName, DevelopmentAccount.UserName, StringComparison.OrdinalIgnoreCase) &&
            password == DevelopmentAccount.Password;
        if (!credentialsAreValid)
            return Results.Redirect("/login?error=invalid_credentials");

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, DevelopmentAccount.UserName),
            new(ClaimTypes.Name, DevelopmentAccount.UserName),
            new(ClaimTypes.Role, DevelopmentAccount.AdministratorRole),
        ];
        ClaimsIdentity identity = new(claims, DevelopmentAccount.AuthenticationScheme);
        ClaimsPrincipal principal = new(identity);
        bool rememberMe = string.Equals(form["remember"], "on", StringComparison.Ordinal);
        AuthenticationProperties properties = new()
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? clock.GetUtcNow().AddDays(7) : null,
            AllowRefresh = true,
        };

        await context.SignInAsync(DevelopmentAccount.AuthenticationScheme, principal, properties);
        return Results.Redirect("/dashboard");
    }

    private static async Task<IResult> Logout(HttpContext context, IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(context);
        await context.SignOutAsync(DevelopmentAccount.AuthenticationScheme);
        return Results.Redirect("/login");
    }
}
