using System.Globalization;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SmsHubNext.Features.Authentication;

public sealed class AdminAccessTokenIssuer
{
    private readonly BearerTokenOptions _options;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _signingCredentials;

    public AdminAccessTokenIssuer(BearerTokenOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(options.Key)),
            SecurityAlgorithms.HmacSha256);
    }

    public AdminAccessTokenResponse Issue(ApiKeyIdentity identity)
    {
        if (identity.AccessLevel != ApiKeyAccessLevel.Administrator)
            throw new ArgumentException("Only administrator identities can receive management tokens.", nameof(identity));

        DateTime issuedAtUtc = _clock.GetUtcNow().UtcDateTime;
        DateTime expiresAtUtc = issuedAtUtc.AddMinutes(_options.AccessTokenExpirationMinutes);
        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, identity.ApiKeyId.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(AuthenticationClaimTypes.ApiKeyId, identity.ApiKeyId.ToString(CultureInfo.InvariantCulture)),
            new Claim(AuthenticationClaimTypes.CustomerId, identity.CustomerId.ToString(CultureInfo.InvariantCulture)),
            new Claim(AuthenticationClaimTypes.KeyPrefix, identity.KeyPrefix),
            new Claim(
                AuthenticationClaimTypes.AccessLevel,
                ((byte)identity.AccessLevel).ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, AuthenticationPolicies.AdministratorRole),
        ];

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAtUtc,
            NotBefore = issuedAtUtc,
            Expires = expiresAtUtc,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _signingCredentials,
        };

        JsonWebTokenHandler handler = new();
        string token = handler.CreateToken(descriptor);
        int expiresInSeconds = checked(_options.AccessTokenExpirationMinutes * 60);

        return new AdminAccessTokenResponse(token, "Bearer", expiresAtUtc, expiresInSeconds);
    }
}
