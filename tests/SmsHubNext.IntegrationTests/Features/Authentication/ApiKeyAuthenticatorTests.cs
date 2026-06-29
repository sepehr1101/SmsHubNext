using DbUp.Engine;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using System.Net;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Authentication;

public sealed class ApiKeyAuthenticatorTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder().Build();
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
    public async Task A_valid_key_resolves_to_its_customer()
    {
        short customerId = await CreateCustomerAsync("auth-ok");
        IssueApiKeyResponse issued = await IssueKeyAsync(customerId);

        Result<ApiKeyIdentity> result = await new ApiKeyAuthenticator(_db, TimeProvider.System)
            .Authenticate(issued.Key, IPAddress.Loopback, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(customerId, result.Value.CustomerId);
        Assert.Equal(issued.Id, result.Value.ApiKeyId);
        Assert.Equal(issued.KeyPrefix, result.Value.KeyPrefix);
    }

    [Fact]
    public async Task A_missing_key_is_unauthorized()
    {
        Result<ApiKeyIdentity> result = await new ApiKeyAuthenticator(_db, TimeProvider.System)
            .Authenticate(null, IPAddress.Loopback, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
        Assert.Equal("auth.missing_key", result.Error.Code);
    }

    [Fact]
    public async Task An_unrecognized_key_is_unauthorized()
    {
        Result<ApiKeyIdentity> result = await new ApiKeyAuthenticator(_db, TimeProvider.System)
            .Authenticate("shn_not-a-real-key", IPAddress.Loopback, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.invalid_key", result.Error!.Code);
    }

    [Fact]
    public async Task An_expired_key_is_unauthorized()
    {
        short customerId = await CreateCustomerAsync("auth-expired");
        IssueApiKeyResponse issued = await IssueKeyAsync(customerId, expiresAtUtc: DateTime.UtcNow.AddDays(-1));

        Result<ApiKeyIdentity> result = await new ApiKeyAuthenticator(_db, TimeProvider.System)
            .Authenticate(issued.Key, IPAddress.Loopback, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.expired_key", result.Error!.Code);
    }

    [Fact]
    public async Task A_caller_outside_the_cidr_allow_list_is_rejected()
    {
        short customerId = await CreateCustomerAsync("auth-cidr");
        IssueApiKeyResponse issued = await IssueKeyAsync(customerId);

        // Restrict the key to a private range only.
        Result<ApiKeyIpRestriction> restriction = await new AddIpRestrictionHandler(_db).Handle(
            issued.Id, new AddIpRestrictionRequest { Cidr = "10.0.0.0/8" }, CancellationToken.None);
        Assert.True(restriction.IsSuccess);

        ApiKeyAuthenticator authenticator = new ApiKeyAuthenticator(_db, TimeProvider.System);

        Result<ApiKeyIdentity> allowed = await authenticator.Authenticate(
            issued.Key, IPAddress.Parse("10.1.2.3"), CancellationToken.None);
        Assert.True(allowed.IsSuccess, allowed.Error?.Message);

        Result<ApiKeyIdentity> denied = await authenticator.Authenticate(
            issued.Key, IPAddress.Parse("203.0.113.7"), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal("auth.ip_not_allowed", denied.Error!.Code);
    }

    private async Task<short> CreateCustomerAsync(string code)
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = code, Code = code }, CancellationToken.None);
        Assert.True(customer.IsSuccess);
        return customer.Value.Id;
    }

    private async Task<IssueApiKeyResponse> IssueKeyAsync(short customerId, DateTime? expiresAtUtc = null)
    {
        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = customerId, Name = "k", ExpiresAtUtc = expiresAtUtc },
            CancellationToken.None);
        Assert.True(key.IsSuccess, key.Error?.Message);
        return key.Value;
    }
}
