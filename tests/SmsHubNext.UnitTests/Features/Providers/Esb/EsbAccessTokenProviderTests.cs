using System.Net;
using System.Net.Http;
using System.Text;
using SmsHubNext.Features.Providers.Esb;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Providers.Esb;

public sealed class EsbAccessTokenProviderTests
{
    [Fact]
    public async Task Concurrent_requests_share_token_until_exact_expiry_then_login_once()
    {
        Clock clock = new();
        using LoginHandler handler = new();
        using EsbAccessTokenProvider provider = new(new ClientFactory(handler), Options(), clock);

        Task<Result<string>>[] requests = Enumerable.Range(0, 50)
            .Select(_ => provider.Get(CancellationToken.None)).ToArray();
        Result<string>[] results = await Task.WhenAll(requests);
        Assert.All(results, result => Assert.Equal("token-1", result.Value));
        Assert.Equal(1, handler.Count);
        Assert.Equal("client_credentials", handler.GrantType);
        Assert.Equal("Basic", handler.AuthScheme);
        Assert.Equal("client:secret", handler.Credentials);

        clock.Advance(TimeSpan.FromSeconds(3599));
        Assert.Equal("token-1", (await provider.Get(CancellationToken.None)).Value);
        Assert.Equal(1, handler.Count);
        clock.Advance(TimeSpan.FromSeconds(1));
        requests = Enumerable.Range(0, 50).Select(_ => provider.Get(CancellationToken.None)).ToArray();
        results = await Task.WhenAll(requests);
        Assert.All(results, result => Assert.Equal("token-2", result.Value));
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"token\",\"expires_in\":0,\"token_type\":\"Bearer\"}")]
    [InlineData("not-json")]
    public async Task Invalid_login_is_not_cached_and_does_not_expose_response(string body)
    {
        using LoginHandler handler = new() { ResponseBody = body };
        using EsbAccessTokenProvider provider = new(new ClientFactory(handler), Options(), new Clock());
        Result<string> first = await provider.Get(CancellationToken.None);
        Result<string> second = await provider.Get(CancellationToken.None);

        Assert.True(first.IsFailure);
        Assert.True(second.IsFailure);
        Assert.Equal("esb.invalid_token_response", first.Error!.Code);
        Assert.Equal(2, handler.Count);
    }

    private static EsbOptions Options() => new() { Enabled = true, Username = "client", Password = "secret" };

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class LoginHandler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? AuthScheme { get; private set; }
        public string? Credentials { get; private set; }
        public string? GrantType { get; private set; }
        public string? ResponseBody { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://esb.abfaisfahan.com:8243/token", request.RequestUri!.AbsoluteUri);
            AuthScheme = request.Headers.Authorization!.Scheme;
            Credentials = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!));
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Equal("grant_type=client_credentials", body);
            GrantType = body.Split('=')[1];
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody ??
                    $"{{\"access_token\":\"token-{Count}\",\"token_type\":\"Bearer\",\"expires_in\":3600}}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
