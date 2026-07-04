using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace SmsHubNext.Shared.Errors;

/// <summary>
/// Last-resort HTTP error boundary. Expected business failures still flow through Result;
/// this catches unhandled exceptions and returns a stable ProblemDetails response.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetails;

    public GlobalExceptionHandler(
        IHostEnvironment environment,
        ILogger<GlobalExceptionHandler> logger,
        IProblemDetailsService problemDetails)
    {
        _environment = environment;
        _logger = logger;
        _problemDetails = problemDetails;
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

        ProblemDetails problemDetails = new ProblemDetails
        {
            Status = problem.StatusCode,
            Title = problem.Title,
            Detail = _environment.IsDevelopment() ? exception.Message : problem.Detail,
            Instance = httpContext.Request.Path,
            Type = "about:blank",
        };
        problemDetails.Extensions["code"] = problem.Code;
        problemDetails.Extensions["traceId"] = TraceId(httpContext);

        if (_environment.IsDevelopment())
            problemDetails.Extensions["exception"] = exception.GetType().Name;

        httpContext.Response.StatusCode = problem.StatusCode;

        bool written = await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails,
        });

        if (!written)
            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static ExceptionProblem ProblemFor(Exception exception) => exception switch
    {
        BadHttpRequestException badRequest => new ExceptionProblem(
            badRequest.StatusCode,
            "Bad request",
            "http.bad_request",
            "The request could not be processed."),

        TimeoutException => new ExceptionProblem(
            StatusCodes.Status503ServiceUnavailable,
            "Service unavailable",
            "server.timeout",
            "The service timed out while processing the request."),

        SqlException => new ExceptionProblem(
            StatusCodes.Status503ServiceUnavailable,
            "Service unavailable",
            "database.unavailable",
            "The database is temporarily unavailable."),

        TaskCanceledException => new ExceptionProblem(
            StatusCodes.Status503ServiceUnavailable,
            "Service unavailable",
            "server.operation_cancelled",
            "The operation was cancelled before it completed."),

        _ => new ExceptionProblem(
            StatusCodes.Status500InternalServerError,
            "Server error",
            "server.unhandled_exception",
            "An unexpected error occurred."),
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

    private sealed record ExceptionProblem(int StatusCode, string Title, string Code, string Detail);
}
