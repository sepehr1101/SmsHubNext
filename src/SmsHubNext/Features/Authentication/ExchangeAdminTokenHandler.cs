using System.Net;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Authentication;

public sealed class ExchangeAdminTokenHandler
{
    private readonly ApiKeyAuthenticator _authenticator;
    private readonly AdminAccessTokenIssuer _tokenIssuer;

    public ExchangeAdminTokenHandler(
        ApiKeyAuthenticator authenticator,
        AdminAccessTokenIssuer tokenIssuer)
    {
        _authenticator = authenticator;
        _tokenIssuer = tokenIssuer;
    }

    public async Task<Result<AdminAccessTokenResponse>> Handle(
        string? rawKey,
        IPAddress? remoteIp,
        CancellationToken cancellationToken)
    {
        Result<ApiKeyIdentity> authentication = await _authenticator.Authenticate(
            rawKey,
            remoteIp,
            cancellationToken);
        if (authentication.IsFailure)
            return authentication.Error!;

        if (authentication.Value.AccessLevel != ApiKeyAccessLevel.Administrator)
        {
            return Error.Forbidden(
                "auth.administrator_key_required",
                UserMessages.Auth.AdministratorKeyRequired);
        }

        return _tokenIssuer.Issue(authentication.Value);
    }
}
