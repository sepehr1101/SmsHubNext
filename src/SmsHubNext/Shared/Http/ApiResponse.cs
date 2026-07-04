using System.Diagnostics;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Shared.Http;

public sealed record ApiResponse<T>(
    bool Success,
    string Code,
    string Message,
    T? Data,
    ApiResponseMeta Meta,
    IReadOnlyList<ApiError>? Errors = null);

public sealed record ApiResponseMeta(string TraceId, DateTime TimestampUtc);

public sealed record ApiError(string? Field, string Code, string Message);

public static class ApiResponse
{
    public static ApiResponse<T> Success<T>(
        T? data,
        HttpContext httpContext,
        int statusCode) => new(
        true,
        SuccessCode(statusCode),
        SuccessMessage(statusCode),
        data,
        Meta(httpContext));

    public static ApiResponse<object> Failure(Error error, HttpContext httpContext) => new(
        false,
        error.Code,
        error.Message,
        null,
        Meta(httpContext));

    public static ApiResponse<object> Failure(
        string code,
        string message,
        HttpContext httpContext,
        IReadOnlyList<ApiError>? errors = null) => new(
        false,
        code,
        message,
        null,
        Meta(httpContext),
        errors);

    private static ApiResponseMeta Meta(HttpContext httpContext)
    {
        TimeProvider clock = httpContext.RequestServices.GetRequiredService<TimeProvider>();
        return new ApiResponseMeta(TraceId(httpContext), clock.GetUtcNow().UtcDateTime);
    }

    private static string TraceId(HttpContext httpContext) =>
        Activity.Current?.Id ?? httpContext.TraceIdentifier;

    private static string SuccessCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status201Created => "created",
        StatusCodes.Status202Accepted => "accepted",
        StatusCodes.Status204NoContent => "no_content",
        _ => "ok",
    };

    private static string SuccessMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status201Created => "Created.",
        StatusCodes.Status202Accepted => "Accepted.",
        StatusCodes.Status204NoContent => "No content.",
        _ => "OK.",
    };
}
