using Dapper;
using DbUp.Engine;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ProviderAccounts;

public sealed class ProviderAccountsTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;
    private ISecretProtector _secretProtector = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();

        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);

        _db = new Db(connectionString);
        _secretProtector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Creates_and_reads_provider_account_without_exposing_secret()
    {
        Result<CreateProviderAccountResponse> created = await CreateMagfaAccount("password-1");
        Assert.True(created.IsSuccess, created.Error?.Message);

        Result<ProviderAccount> read = await new GetProviderAccountHandler(_db).Handle(
            created.Value.Id,
            CancellationToken.None);

        Assert.True(read.IsSuccess, read.Error?.Message);
        Assert.Equal("magfa", read.Value.ProviderCode);
        Assert.Equal("Magfa Main Account", read.Value.DisplayName);
        Assert.Equal(ProviderAccountAuthTypes.UsernamePasswordDomain, read.Value.AuthType);
        Assert.Equal("user", read.Value.Settings["username"]);
        Assert.Equal("domain", read.Value.Settings["domain"]);
        Assert.True(read.Value.HasSecret);
        Assert.True(read.Value.IsActive);
    }

    [Fact]
    public async Task Updates_without_secret_keep_existing_secret_encrypted_value()
    {
        Result<CreateProviderAccountResponse> created = await CreateMagfaAccount("password-1");
        Assert.True(created.IsSuccess, created.Error?.Message);

        byte[] before = await SecretEncrypted(created.Value.Id);

        Result updated = await new UpdateProviderAccountHandler(_db, _secretProtector).Handle(
            created.Value.Id,
            new UpdateProviderAccountRequest
            {
                ProviderCode = "magfa",
                DisplayName = "Magfa Renamed",
                AuthType = ProviderAccountAuthTypes.UsernamePasswordDomain,
                Settings = new Dictionary<string, string>
                {
                    ["username"] = "user2",
                    ["domain"] = "domain2",
                },
                IsActive = true,
            },
            CancellationToken.None);

        Assert.True(updated.IsSuccess, updated.Error?.Message);
        byte[] after = await SecretEncrypted(created.Value.Id);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Sender_line_can_be_associated_with_provider_account()
    {
        Result<CreateProviderAccountResponse> account = await CreateMagfaAccount("password-1");
        Assert.True(account.IsSuccess, account.Error?.Message);

        Result<CreateSenderLineResponse> senderLine = await new CreateSenderLineHandler(_db).Handle(
            new CreateSenderLineRequest
            {
                ProviderId = 1,
                LineNumber = "30008888",
                IsSharedLine = true,
                ProviderAccountId = account.Value.Id,
            },
            CancellationToken.None);

        Assert.True(senderLine.IsSuccess, senderLine.Error?.Message);

        Result<IReadOnlyList<SenderLine>> listed = await new ListSenderLinesHandler(_db).Handle(CancellationToken.None);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        SenderLine line = Assert.Single(listed.Value, item => item.LineNumber == "30008888");
        Assert.Equal(account.Value.Id, line.ProviderAccountId);
    }

    private async Task<Result<CreateProviderAccountResponse>> CreateMagfaAccount(string secret) =>
        await new CreateProviderAccountHandler(_db, _secretProtector).Handle(
            new CreateProviderAccountRequest
            {
                ProviderCode = "magfa",
                DisplayName = "Magfa Main Account",
                AuthType = ProviderAccountAuthTypes.UsernamePasswordDomain,
                Settings = new Dictionary<string, string>
                {
                    ["username"] = "user",
                    ["domain"] = "domain",
                },
                Secret = secret,
            },
            CancellationToken.None);

    private async Task<byte[]> SecretEncrypted(int providerAccountId)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);

        byte[]? secret = await connection.ExecuteScalarAsync<byte[]>(new CommandDefinition(
            "SELECT SecretEncrypted FROM dbo.ProviderAccount WHERE Id = @providerAccountId;",
            new { providerAccountId },
            cancellationToken: CancellationToken.None));

        Assert.NotNull(secret);
        return secret;
    }
}
