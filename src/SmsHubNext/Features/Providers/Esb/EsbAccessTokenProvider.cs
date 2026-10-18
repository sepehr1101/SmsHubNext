using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.Esb;

/// <summary>
/// Process-wide OAuth token cache. The lock covers login, so concurrent dispatches
/// share a single request. Expiry uses the login start time and expires_in, never
/// a hard-coded hour or an unvalidated JWT claim. Tokens are never persisted or logged.
/// </summary>
public sealed class EsbAccessTokenProvider(
    IHttpClientFactory clients, EsbOptions options, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private TokenSnapshot? _cachedToken;

    public async Task<Result<string>> Get(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return Error.Provider("esb.disabled", "اتصال ESB فعال نیست.");

        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && _cachedToken.ExpiresAt > clock.GetUtcNow())
                return _cachedToken.Value;
            return await Login(cancellationToken);
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task<Result<string>> Login(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = clock.GetUtcNow();
        using HttpClient client = clients.CreateClient(EsbOptions.HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Post, options.LoginUrl);
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Username + ":" + options.Password));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
        });

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Error.Provider("esb.login_failed", "دریافت توکن ESB ناموفق بود.");
            TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0 ||
                !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return Error.Provider("esb.invalid_token_response", "پاسخ ورود ESB معتبر نیست.");
            }

            DateTimeOffset expiresAt = startedAt.AddSeconds(token.ExpiresIn);
            if (expiresAt <= clock.GetUtcNow())
                return Error.Provider("esb.expired_token_response", "توکن دریافتی ESB منقضی شده است.");
            _cachedToken = new TokenSnapshot(token.AccessToken, expiresAt);
            return token.AccessToken;
        }
        catch (HttpRequestException)
        {
            return Error.Provider("esb.login_transport", "ارتباط با سرویس ورود ESB برقرار نشد.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Provider("esb.login_timeout", "زمان انتظار برای ورود ESB به پایان رسید.");
        }
        catch (JsonException)
        {
            return Error.Provider("esb.invalid_token_response", "پاسخ ورود ESB معتبر نیست.");
        }
    }

    public void Dispose() => _loginLock.Dispose();

    private sealed record TokenSnapshot(string Value, DateTimeOffset ExpiresAt);
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
