using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Shared.Errors;

/// <summary>
/// Last-resort HTTP error boundary. Expected business failures still flow through Result;
/// this catches unhandled exceptions and returns a stable API envelope response.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IHostEnvironment environment,
        ILogger<GlobalExceptionHandler> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
            return false;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                exception,
                "Request was aborted by the client. TraceId: {TraceId}",
                TraceId(httpContext));
            return true;
        }

        ExceptionProblem problem = ProblemFor(exception);
        LogException(problem, exception, httpContext);

        string message = _environment.IsDevelopment() ? exception.Message : problem.Detail;
        httpContext.Response.StatusCode = problem.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            ApiResponse.Failure(problem.Code, message, httpContext),
            cancellationToken);

        return true;
    }

    private static ExceptionProblem ProblemFor(Exception exception) => exception switch
    {
        BadHttpRequestException badRequest => new ExceptionProblem(
            badRequest.StatusCode,
            "http.bad_request",
            UserMessages.Common.BadRequest),

        TimeoutException => new ExceptionProblem(
            StatusCodes.Status503ServiceUnavailable,
            "server.timeout",
            UserMessages.Common.Timeout),

        SqlException => new ExceptionProblem(
            StatusCodes.Status503ServiceUnavailable,
            "database.unavailable",
            UserMessages.Common.DatabaseUnavailable),

        TaskCanceledException => new ExceptionProblem(
            StatusCodes.Status503ServiceUnavailable,
            "server.operation_cancelled",
            UserMessages.Common.OperationCancelled),

        _ => new ExceptionProblem(
            StatusCodes.Status500InternalServerError,
            "server.unhandled_exception",
            UserMessages.Common.UnexpectedError),
    };

    private void LogException(ExceptionProblem problem, Exception exception, HttpContext httpContext)
    {
        if (problem.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception returned {StatusCode}. TraceId: {TraceId}",
                problem.StatusCode,
                TraceId(httpContext));
            return;
        }

        _logger.LogWarning(
            exception,
            "Request exception returned {StatusCode}. TraceId: {TraceId}",
            problem.StatusCode,
            TraceId(httpContext));
    }

    private static string TraceId(HttpContext httpContext) =>
        Activity.Current?.Id ?? httpContext.TraceIdentifier;

    private sealed record ExceptionProblem(int StatusCode, string Code, string Detail);
}
