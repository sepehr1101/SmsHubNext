namespace SmsHubNext.Shared.Results;

/// <summary>
/// Category of an expected failure. Drives the HTTP status code at the edge
/// (see <c>ResultActionResults</c> and ARCHITECTURE.md §7).
/// </summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Provider,
    Unexpected,
}

/// <summary>
/// An expected failure: a stable machine <paramref name="Code"/>, a human-readable
/// <paramref name="Message"/>, and a <paramref name="Type"/> that maps to an HTTP status.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Provider(string code, string message) => new(code, message, ErrorType.Provider);
    public static Error Unexpected(string code, string message) => new(code, message, ErrorType.Unexpected);
}

/// <summary>Outcome of an operation that can fail in an expected way and yields no value.</summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
            throw new InvalidOperationException("A successful result cannot carry an error.");
        if (!isSuccess && error is null)
            throw new InvalidOperationException("A failed result must carry an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure; non-null exactly when <see cref="IsFailure"/>.</summary>
    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);

    /// <summary>
    /// Success carrying a value. Prefer this for interface-typed values
    /// (e.g. <c>IReadOnlyList&lt;T&gt;</c>): C# does not apply the implicit
    /// <c>T → Result&lt;T&gt;</c> conversion when <c>T</c> is an interface.
    /// </summary>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    /// <summary>Failure for a value-bearing result.</summary>
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Outcome that carries a <typeparamref name="T"/> value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T _value;

    private Result(bool isSuccess, T value, Error? error) : base(isSuccess, error) => _value = value;

    /// <summary>The value; valid only when <see cref="Result.IsSuccess"/> (throws otherwise).</summary>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot read the value of a failed result.");

    public static Result<T> Success(T value) => new(true, value, null);
    public static new Result<T> Failure(Error error) => new(false, default!, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
