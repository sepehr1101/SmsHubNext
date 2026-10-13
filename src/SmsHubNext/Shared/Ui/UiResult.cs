namespace SmsHubNext.Shared.Ui;

/// <summary>A page-friendly projection of a feature Result, with no HTTP transport.</summary>
public sealed record UiResult<T>(bool IsSuccess, T? Value, string? ErrorMessage)
{
    public static UiResult<T> Success(T? value) => new(true, value, null);
    public static UiResult<T> Failure(string message) => new(false, default, message);
}
