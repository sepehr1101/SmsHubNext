using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Shared.Http;

[ApiController]
public abstract class BaseController : ControllerBase
{
    protected IActionResult FromResult(Result result) =>
        result.IsSuccess
            ? FromData<string?>(null, StatusCodes.Status200OK)
            : FromError(result.Error!);

    protected IActionResult FromResult<T>(Result<T> result) =>
        result.IsSuccess
            ? FromData(result.Value, StatusCodes.Status200OK)
            : FromError(result.Error!);

    protected IActionResult FromResult<T>(Result<T> result, int successStatusCode) =>
        result.IsSuccess
            ? FromData(result.Value, successStatusCode)
            : FromError(result.Error!);

    protected IActionResult FromError(Error error)
    {
        int statusCode = ApiStatusCodes.For(error.Type);
        return Envelope(ApiResponse.Failure(error, HttpContext), statusCode);
    }

    private IActionResult FromData<T>(T? data, int statusCode) =>
        Envelope(ApiResponse.Success(data, HttpContext, statusCode), statusCode);

    private static IActionResult Envelope<T>(ApiResponse<T> response, int statusCode) =>
        new ObjectResult(response) { StatusCode = statusCode };
}
