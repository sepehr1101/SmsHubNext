using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace SmsHubNext.App.Shared.Api;

public sealed class ManagementAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SmsHubApiOptions _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public ManagementAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<SmsHubApiOptions> options,
        TimeProvider clock)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _clock = clock;
    }

    public async Task<string> Get(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > _clock.GetUtcNow().AddSeconds(30))
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > _clock.GetUtcNow().AddSeconds(30))
                return _accessToken;

            using HttpClient httpClient = _httpClientFactory.CreateClient();
            using HttpRequestMessage request = new(HttpMethod.Post, BuildUri("auth/token"));
            request.Headers.Add("X-Api-Key", _options.AdminApiKey);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            ApiEnvelope<AdminTokenResponse>? envelope =
                await response.Content.ReadFromJsonAsync<ApiEnvelope<AdminTokenResponse>>(cancellationToken);

            if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
                throw new InvalidOperationException(envelope?.Message ?? "دریافت توکن مدیریتی ناموفق بود.");

            _accessToken = envelope.Data.AccessToken;
            _accessTokenExpiresAt = new DateTimeOffset(envelope.Data.ExpiresAtUtc, TimeSpan.Zero);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path) =>
        new(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    private sealed record AdminTokenResponse(
        string AccessToken,
        string TokenType,
        DateTime ExpiresAtUtc,
        int ExpiresInSeconds);
}
