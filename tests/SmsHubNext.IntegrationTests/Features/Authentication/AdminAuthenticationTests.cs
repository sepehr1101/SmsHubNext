using DbUp.Engine;
using SmsHubNext.Deployment;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using System.Net;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Authentication;

public sealed class AdminAuthenticationTests : IAsyncLifetime
{
    private const string SigningKey =
        "an-integration-test-signing-key-that-is-long-enough-for-hmac-sha256";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();

        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);
        _db = new Db(connectionString);
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Bootstrapped_key_can_exchange_for_a_management_token_and_system_customer_stays_hidden()
    {
        SetupAdministratorCredential credential = await new AdminApiKeyBootstrapper(_db).CreateAsync(
            "First administrator",
            expiresAtUtc: null,
            CancellationToken.None);
        ApiKeyAuthenticator authenticator = new(_db, TimeProvider.System);
        Result<ApiKeyIdentity> authenticated = await authenticator.Authenticate(
            credential.ApiKey,
            IPAddress.Loopback,
            CancellationToken.None);

        Assert.True(authenticated.IsSuccess, authenticated.Error?.Message);
        Assert.Equal(ApiKeyAccessLevel.Administrator, authenticated.Value.AccessLevel);
        Assert.Equal(credential.Id, authenticated.Value.ApiKeyId);

        BearerTokenOptions options = new()
        {
            Key = SigningKey,
            Issuer = "SmsHubNext.IntegrationTests",
            Audience = "SmsHubNext.Administration",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationMinutes = 60,
        };
        ExchangeAdminTokenHandler exchange = new(
            authenticator,
            new AdminAccessTokenIssuer(options, TimeProvider.System));
        Result<AdminAccessTokenResponse> token = await exchange.Handle(
            credential.ApiKey,
            IPAddress.Loopback,
            CancellationToken.None);

        Assert.True(token.IsSuccess, token.Error?.Message);
        Assert.False(string.IsNullOrWhiteSpace(token.Value.AccessToken));

        Result<IReadOnlyList<Customer>> customers = await new ListCustomersHandler(_db)
            .Handle(CancellationToken.None);
        Assert.True(customers.IsSuccess);
        Assert.Empty(customers.Value);
    }

    [Fact]
    public async Task Customer_key_cannot_exchange_for_a_management_token()
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest { Name = "Tenant", Code = "tenant" },
            CancellationToken.None);
        Assert.True(customer.IsSuccess);

        SmsHubNext.Features.ApiKeys.IssueApiKeyResponse key =
            (await new SmsHubNext.Features.ApiKeys.IssueApiKeyHandler(_db).Handle(
                new SmsHubNext.Features.ApiKeys.IssueApiKeyRequest
                {
                    CustomerId = customer.Value.Id,
                    Name = "customer-key",
                },
                CancellationToken.None)).Value;
        BearerTokenOptions options = new()
        {
            Key = SigningKey,
            Issuer = "SmsHubNext.IntegrationTests",
            Audience = "SmsHubNext.Administration",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationMinutes = 60,
        };
        ExchangeAdminTokenHandler exchange = new(
            new ApiKeyAuthenticator(_db, TimeProvider.System),
            new AdminAccessTokenIssuer(options, TimeProvider.System));

        Result<AdminAccessTokenResponse> token = await exchange.Handle(
            key.Key,
            IPAddress.Loopback,
            CancellationToken.None);

        Assert.True(token.IsFailure);
        Assert.Equal(ErrorType.Forbidden, token.Error!.Type);
        Assert.Equal("auth.administrator_key_required", token.Error.Code);
    }
}
