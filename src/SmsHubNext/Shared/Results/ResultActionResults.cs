using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SmsHubNext.Shared.Results;

/// <summary>
/// The single place that turns a <see cref="Result"/> into an HTTP response
/// (ARCHITECTURE.md §7). Controllers call <c>result.ToActionResult()</c>:
/// success → 2xx, failure → <see cref="ProblemDetails"/> with the status
/// implied by the <see cref="ErrorType"/>.
/// </summary>
public static class ResultActionResults
{
    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new OkResult() : Problem(result.Error!);

    public static IActionResult ToActionResult<T>(this Result<T> result) =>
        result.IsSuccess ? new OkObjectResult(result.Value) : Problem(result.Error!);

    /// <summary>Maps success to a specific 2xx code (e.g. 201 Created, 202 Accepted).</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result, int successStatusCode) =>
        result.IsSuccess
            ? new ObjectResult(result.Value) { StatusCode = successStatusCode }
            : Problem(result.Error!);

    private static IActionResult Problem(Error error)
    {
        var status = StatusCodeFor(error.Type);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = error.Type.ToString(),
            Detail = error.Message,
        };
        problem.Extensions["code"] = error.Code;
        return new ObjectResult(problem) { StatusCode = status };
    }

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Provider => StatusCodes.Status502BadGateway,
        ErrorType.Unexpected => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError,
    };
}
