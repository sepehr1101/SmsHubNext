using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

[ApiController]
[Route("messages")]
public sealed class CustomerMessagesController(ApiKeyAuthenticator authenticator, SearchMessagesHandler handler) : BaseController
{
    /// <summary>Find the calling customer's outbound messages by bill ID or mobile number.</summary>
    /// <remarks>Requires X-Api-Key. Dates are inclusive Jalali dates; results are paged in groups of 25.</remarks>
    [HttpGet]
    [AllowAnonymous] // Customer API key is checked explicitly; management JWTs/cookies do not authorize this route.
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Find([FromQuery] FindCustomerMessagesRequest request, CancellationToken cancellationToken)
    {
        Result<ApiKeyIdentity> identity = await authenticator.Authenticate(
            Request.Headers[ApiKeyConstants.HeaderName].ToString(), HttpContext.Connection.RemoteIpAddress, cancellationToken);
        if (identity.IsFailure) return FromResult(identity);
        if (identity.Value.AccessLevel != ApiKeyAccessLevel.Customer)
            return FromError(Error.Forbidden("reports.customer_key_required", "برای مشاهدهٔ پیام‌ها از کلید API مشتری استفاده کنید."));
        Result<SearchMessagesRequest> query = request.ToSearch();
        if (query.IsFailure) return FromResult(query);
        return FromResult(await handler.HandleForCustomer(query.Value, identity.Value, cancellationToken));
    }
}
