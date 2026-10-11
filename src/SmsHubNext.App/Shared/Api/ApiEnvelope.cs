namespace SmsHubNext.App.Shared.Api;

public sealed record ApiEnvelope<T>(
    bool Success,
    string Code,
    string Message,
    T? Data,
    ApiResponseMeta Meta,
    IReadOnlyList<ApiError>? Errors = null);

public sealed record ApiResponseMeta(string TraceId, DateTime TimestampUtc);
public sealed record ApiError(string? Field, string Code, string Message);

public sealed record ApiClientResult<T>(bool IsSuccess, T? Value, string? ErrorMessage)
{
    public static ApiClientResult<T> Success(T? value) => new(true, value, null);
    public static ApiClientResult<T> Failure(string message) => new(false, default, message);
}
