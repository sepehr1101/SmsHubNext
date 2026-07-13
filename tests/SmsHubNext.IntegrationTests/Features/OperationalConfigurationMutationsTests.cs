using System.Net;
using Dapper;
using DbUp.Engine;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Magfa;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.Tariffs;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features;

public sealed class OperationalConfigurationMutationsTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;
    private ISecretProtector _secretProtector = null!;
    private short _customerId;
    private int _actorApiKeyId;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();
        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);

        _db = new Db(connectionString);
        _secretProtector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest { Name = "Configuration admin", Code = $"admin-{Guid.NewGuid():N}" },
            CancellationToken.None);
        Assert.True(customer.IsSuccess);
        _customerId = customer.Value.Id;

        Result<IssueApiKeyResponse> actor = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = _customerId, Name = "actor" },
            CancellationToken.None);
        Assert.True(actor.IsSuccess);
        _actorApiKeyId = actor.Value.Id;
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Tariff_versions_can_be_created_closed_and_soft_deleted_without_price_history_rewrites()
    {
        DateTime nowUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
        DateTime effectiveFrom = nowUtc.AddDays(-1);
        CreateTariffHandler create = new CreateTariffHandler(_db);
        Result<CreateTariffResponse> created = await create.Handle(
            TariffRequest(effectiveFrom, 1250m),
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);

        Result<CostQuote> quote = await new QuoteHandler(_db).Handle(
            new QuoteRequest { ProviderId = 1, Text = "Hello" },
            CancellationToken.None);
        Assert.True(quote.IsSuccess, quote.Error?.Message);
        Assert.Equal(1250m, quote.Value.UnitPrice);

        Result<CreateTariffResponse> overlap = await create.Handle(
            TariffRequest(effectiveFrom.AddHours(1), 1300m),
            CancellationToken.None);
        Assert.True(overlap.IsFailure);
        Assert.Equal(ErrorType.Conflict, overlap.Error!.Type);

        Result updated = await new UpdateTariffHandler(_db).Handle(
            created.Value.Id,
            new UpdateTariffRequest { EffectiveToUtc = nowUtc.AddDays(1), IsActive = true },
            CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);

        Result deleted = await new DeleteTariffHandler(_db).Handle(
            created.Value.Id,
            _actorApiKeyId,
            CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);

        Result<IReadOnlyList<TariffResponse>> listed = await new ListTariffsHandler(_db).Handle(CancellationToken.None);
        Assert.DoesNotContain(listed.Value, tariff => tariff.Id == created.Value.Id);
        await AssertDeleteAudit("Tariff", created.Value.Id);

        Result<CostQuote> afterDelete = await new QuoteHandler(_db).Handle(
            new QuoteRequest { ProviderId = 1, Text = "Hello" },
            CancellationToken.None);
        Assert.True(afterDelete.IsFailure);
    }

    [Fact]
    public async Task Provider_account_soft_delete_hides_it_and_removes_it_from_provider_resolution()
    {
        Result<CreateProviderAccountResponse> created = await CreateProviderAccount();
        Assert.True(created.IsSuccess, created.Error?.Message);

        Result deleted = await new DeleteProviderAccountHandler(_db).Handle(
            created.Value.Id,
            _actorApiKeyId,
            CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);

        Result<IReadOnlyList<ProviderAccount>> listed = await new ListProviderAccountsHandler(_db).Handle(
            CancellationToken.None);
        Assert.DoesNotContain(listed.Value, account => account.Id == created.Value.Id);

        Result<ProviderAccount> get = await new GetProviderAccountHandler(_db).Handle(
            created.Value.Id,
            CancellationToken.None);
        Assert.True(get.IsFailure);
        await AssertDeleteAudit("ProviderAccount", created.Value.Id);

        MagfaAccountResolver resolver = new MagfaAccountResolver(_db, _secretProtector);
        Assert.Empty(await resolver.GetAccountsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Api_key_can_be_updated_then_revoked_with_actor_attribution()
    {
        Result<IssueApiKeyResponse> target = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = _customerId, Name = "old-name" },
            CancellationToken.None);
        Assert.True(target.IsSuccess);

        DateTime expiresAtUtc = TimeProvider.System.GetUtcNow().UtcDateTime.AddDays(30);
        Result updated = await new UpdateApiKeyHandler(_db).Handle(
            target.Value.Id,
            new UpdateApiKeyRequest { Name = "new-name", ExpiresAtUtc = expiresAtUtc, IsActive = true },
            CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);

        Result revoked = await new RevokeApiKeyHandler(_db).Handle(
            target.Value.Id,
            _actorApiKeyId,
            CancellationToken.None);
        Assert.True(revoked.IsSuccess, revoked.Error?.Message);

        Result<IReadOnlyList<ApiKey>> listed = await new ListApiKeysHandler(_db).Handle(
            _customerId,
            CancellationToken.None);
        ApiKey key = Assert.Single(listed.Value, item => item.Id == target.Value.Id);
        Assert.Equal("new-name", key.Name);
        Assert.NotNull(key.RevokedAtUtc);
        Assert.Equal(_actorApiKeyId, key.RevokedByApiKeyId);
        Assert.False(key.IsActive);

        Result<ApiKeyIdentity> authentication = await new ApiKeyAuthenticator(_db, TimeProvider.System).Authenticate(
            target.Value.Key,
            null,
            CancellationToken.None);
        Assert.True(authentication.IsFailure);

        Result rejectedUpdate = await new UpdateApiKeyHandler(_db).Handle(
            target.Value.Id,
            new UpdateApiKeyRequest { Name = "cannot-change", IsActive = true },
            CancellationToken.None);
        Assert.True(rejectedUpdate.IsFailure);
        Assert.Equal(ErrorType.Conflict, rejectedUpdate.Error!.Type);
    }

    [Fact]
    public async Task Ip_restriction_update_and_soft_delete_change_authentication_immediately()
    {
        Result<IssueApiKeyResponse> target = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = _customerId, Name = "restricted" },
            CancellationToken.None);
        Assert.True(target.IsSuccess);

        Result<ApiKeyIpRestriction> added = await new AddIpRestrictionHandler(_db).Handle(
            target.Value.Id,
            new AddIpRestrictionRequest { Cidr = "10.0.0.0/8", Description = "old" },
            CancellationToken.None);
        Assert.True(added.IsSuccess, added.Error?.Message);

        Result updated = await new UpdateIpRestrictionHandler(_db).Handle(
            target.Value.Id,
            added.Value.Id,
            new UpdateIpRestrictionRequest { Cidr = "192.168.0.0/16", Description = "new" },
            CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);

        ApiKeyAuthenticator authenticator = new ApiKeyAuthenticator(_db, TimeProvider.System);
        Result<ApiKeyIdentity> oldNetwork = await authenticator.Authenticate(
            target.Value.Key,
            IPAddress.Parse("10.1.2.3"),
            CancellationToken.None);
        Assert.True(oldNetwork.IsFailure);

        Result<ApiKeyIdentity> newNetwork = await authenticator.Authenticate(
            target.Value.Key,
            IPAddress.Parse("192.168.1.2"),
            CancellationToken.None);
        Assert.True(newNetwork.IsSuccess);

        Result deleted = await new DeleteIpRestrictionHandler(_db).Handle(
            target.Value.Id,
            added.Value.Id,
            _actorApiKeyId,
            CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);

        Result<IReadOnlyList<ApiKeyIpRestriction>> listed = await new ListIpRestrictionsHandler(_db).Handle(
            target.Value.Id,
            CancellationToken.None);
        Assert.Empty(listed.Value);
        await AssertDeleteAudit("ApiKeyIpRestriction", added.Value.Id);

        Result<ApiKeyIdentity> unrestricted = await authenticator.Authenticate(
            target.Value.Key,
            IPAddress.Parse("203.0.113.10"),
            CancellationToken.None);
        Assert.True(unrestricted.IsSuccess);
    }

    private static CreateTariffRequest TariffRequest(DateTime effectiveFromUtc, decimal price) => new()
    {
        ProviderId = 1,
        Encoding = SmsEncoding.Gsm7,
        EffectiveFromUtc = effectiveFromUtc,
        Rates = new[]
        {
            new CreateTariffRateRequest { MinChars = 1, MaxChars = null, PricePerSegment = price },
        },
    };

    private async Task<Result<CreateProviderAccountResponse>> CreateProviderAccount() =>
        await new CreateProviderAccountHandler(_db, _secretProtector).Handle(
            new CreateProviderAccountRequest
            {
                ProviderCode = "magfa",
                DisplayName = "Deletable account",
                AuthType = ProviderAccountAuthTypes.UsernamePasswordDomain,
                Settings = new Dictionary<string, string>
                {
                    ["username"] = "user",
                    ["domain"] = "domain",
                },
                Secret = "secret",
            },
            CancellationToken.None);

    private async Task AssertDeleteAudit(string tableName, object id)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);
        DeleteAuditRow row = await connection.QuerySingleAsync<DeleteAuditRow>(
            $"SELECT DeletedAtUtc, DeletedByApiKeyId FROM dbo.[{tableName}] WHERE Id = @Id;",
            new { Id = id });

        Assert.NotEqual(default, row.DeletedAtUtc);
        Assert.Equal(_actorApiKeyId, row.DeletedByApiKeyId);
    }

    private sealed record DeleteAuditRow(DateTime DeletedAtUtc, int DeletedByApiKeyId);
}
