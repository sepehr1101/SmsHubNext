using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Authentication;

/// <summary>
/// Enforces API-key authentication: resolves the <c>X-Api-Key</c> header via
/// <see cref="ApiKeyAuthenticator"/>, rejects the request with 401 on failure, and
/// otherwise stashes the <see cref="ApiKeyIdentity"/> on <c>HttpContext.Items</c> for
/// handlers to read (<see cref="HttpContextApiKeyExtensions.GetApiKeyIdentity"/>).
///
/// NOT REGISTERED YET — auth is implemented but intentionally inactive so the APIs
/// stay open for testing. Activate by calling <c>app.UseApiKeyAuthentication()</c> in
/// the pipeline (see <c>ApplicationBuilderExtensions</c>) and switching the send/
/// delivery handlers from the interim explicit ids to the resolved identity.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ApiKeyAuthenticator authenticator)
    {
        var rawKey = context.Request.Headers[ApiKeyConstants.HeaderName].ToString();

        var result = await authenticator.Authenticate(
            rawKey,
            context.Connection.RemoteIpAddress,
            context.RequestAborted);

        if (result.IsFailure)
        {
            var error = result.Error!;
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = error.Type.ToString(),
                Detail = error.Message,
            };
            problem.Extensions["code"] = error.Code;
            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
            return;
        }

        context.Items[ApiKeyConstants.HttpContextItemKey] = result.Value;
        await _next(context);
    }
}
