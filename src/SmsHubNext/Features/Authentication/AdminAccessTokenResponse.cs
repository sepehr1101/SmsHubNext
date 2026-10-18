namespace SmsHubNext.Features.Authentication;

public sealed record AdminAccessTokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    int ExpiresInSeconds);
