using DbUp.Engine;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using System.Net;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.GeoSections;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ApiKeys;

public sealed class ApiKeysTests : IAsyncLifetime
{
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
    public async Task Issues_a_key_then_lists_it_without_the_secret()
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "Tenant", Code = "tenant" }, CancellationToken.None);
        Assert.True(customer.IsSuccess);

        Result<IssueApiKeyResponse> issued = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customer.Value.Id, Name = "Prod" }, CancellationToken.None);
        Assert.True(issued.IsSuccess);
        Assert.StartsWith("shn_", issued.Value.Key);
        Assert.Equal(issued.Value.Key[..12], issued.Value.KeyPrefix);

        Result<IReadOnlyList<ApiKey>> keys = await new ListApiKeysHandler(_db).Handle(customer.Value.Id, CancellationToken.None);
        Assert.True(keys.IsSuccess);
        ApiKey key = Assert.Single(keys.Value);
        Assert.Equal(issued.Value.Id, key.Id);
        Assert.Equal("Prod", key.Name);
        Assert.Equal(issued.Value.KeyPrefix, key.KeyPrefix);
    }

    [Fact]
    public async Task Renew_replaces_secret_preserves_identity_and_rejects_revoked_keys()
    {
        CancellationToken token = CancellationToken.None;
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "Renew", Code = "renew" }, token);
        Assert.True(customer.IsSuccess);
        Result<IssueApiKeyResponse> issued = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customer.Value.Id, Name = "Production" }, token);
        Assert.True(issued.IsSuccess);
        ApiKeyAuthenticator authenticator = new(_db, TimeProvider.System);
        Result<ApiKeyIpRestriction> restriction = await new AddIpRestrictionHandler(_db)
            .Handle(issued.Value.Id, new AddIpRestrictionRequest { Cidr = "127.0.0.1/32" }, token);
        Assert.True(restriction.IsSuccess);
        Assert.True((await authenticator.Authenticate(issued.Value.Key, IPAddress.Loopback, token)).IsSuccess);
        ApiKey before = Assert.Single((await new ListApiKeysHandler(_db).Handle(customer.Value.Id, token)).Value);

        Result<IssueApiKeyResponse> renewed = await new RenewApiKeyHandler(_db).Handle(issued.Value.Id, token);

        Assert.True(renewed.IsSuccess);
        Assert.Equal(issued.Value.Id, renewed.Value.Id);
        Assert.NotEqual(issued.Value.Key, renewed.Value.Key);
        Assert.Equal(renewed.Value.Key[..12], renewed.Value.KeyPrefix);
        Assert.True((await authenticator.Authenticate(issued.Value.Key, IPAddress.Loopback, token)).IsFailure);
        Result<ApiKeyIdentity> identity = await authenticator.Authenticate(renewed.Value.Key, IPAddress.Loopback, token);
        Assert.True(identity.IsSuccess);
        Assert.Equal(issued.Value.Id, identity.Value.ApiKeyId);
        Result<ApiKeyIdentity> disallowed = await authenticator.Authenticate(renewed.Value.Key, IPAddress.Parse("192.0.2.1"), token);
        Assert.Equal("auth.ip_not_allowed", disallowed.Error!.Code);
        ApiKey after = Assert.Single((await new ListApiKeysHandler(_db).Handle(customer.Value.Id, token)).Value);
        Assert.Equal(before with { KeyPrefix = renewed.Value.KeyPrefix }, after);

        Assert.True((await new RevokeApiKeyHandler(_db).Handle(issued.Value.Id, issued.Value.Id, token)).IsSuccess);
        Result<IssueApiKeyResponse> revoked = await new RenewApiKeyHandler(_db).Handle(issued.Value.Id, token);
        Assert.True(revoked.IsFailure);
        Assert.Equal("api_keys.revoked", revoked.Error!.Code);
        Assert.True((await authenticator.Authenticate(renewed.Value.Key, IPAddress.Loopback, token)).IsFailure);
    }

    [Fact]
    public async Task Renew_rejects_invalid_and_unknown_ids()
    {
        RenewApiKeyHandler handler = new(_db);
        Result<IssueApiKeyResponse> invalid = await handler.Handle(0, CancellationToken.None);
        Result<IssueApiKeyResponse> missing = await handler.Handle(int.MaxValue, CancellationToken.None);
        Assert.Equal(ErrorType.Validation, invalid.Error!.Type);
        Assert.Equal(ErrorType.NotFound, missing.Error!.Type);
    }

    [Fact]
    public async Task Rejects_an_unknown_customer()
    {
        Result<IssueApiKeyResponse> result = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = 32000, Name = "Nope" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }
}
