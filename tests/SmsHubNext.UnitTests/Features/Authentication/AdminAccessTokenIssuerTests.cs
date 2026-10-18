using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmsHubNext.Features.Authentication;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Authentication;

public sealed class AdminAccessTokenIssuerTests
{
    private const string SigningKey =
        "a-test-signing-key-that-is-deliberately-long-enough-for-hmac-sha256";

    [Fact]
    public async Task Issued_token_is_valid_and_reconstructs_the_api_key_identity()
    {
        DateTimeOffset now = new(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);
        FixedTimeProvider clock = new(now);
        BearerTokenOptions options = Options();
        AdminAccessTokenIssuer issuer = new(options, clock);
        ApiKeyIdentity expected = new(17, 4, "shn_example", ApiKeyAccessLevel.Administrator);

        AdminAccessTokenResponse response = issuer.Issue(expected);

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(now.UtcDateTime.AddMinutes(15), response.ExpiresAtUtc);
        Assert.Equal(900, response.ExpiresInSeconds);

        JsonWebTokenHandler handler = new();
        TokenValidationParameters validationParameters = new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero,
        };
        TokenValidationResult validation = await handler.ValidateTokenAsync(
            response.AccessToken,
            validationParameters);

        Assert.True(validation.IsValid, validation.Exception?.Message);
        DefaultHttpContext context = new()
        {
            User = new ClaimsPrincipal(validation.ClaimsIdentity),
        };

        ApiKeyIdentity? actual = context.GetApiKeyIdentity();
        Assert.Equal(expected, actual);
        Assert.True(context.User.IsInRole(AuthenticationPolicies.AdministratorRole));
    }

    [Fact]
    public void Customer_identity_cannot_receive_a_management_token()
    {
        AdminAccessTokenIssuer issuer = new(Options(), TimeProvider.System);
        ApiKeyIdentity customer = new(1, 2, "shn_customer", ApiKeyAccessLevel.Customer);

        Assert.Throws<ArgumentException>(() => issuer.Issue(customer));
    }

    private static BearerTokenOptions Options() => new()
    {
        Key = SigningKey,
        Issuer = "SmsHubNext.UnitTests",
        Audience = "SmsHubNext.Administration",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationMinutes = 60,
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
