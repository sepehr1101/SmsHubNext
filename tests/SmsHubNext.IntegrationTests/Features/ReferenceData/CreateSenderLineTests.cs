using DbUp.Engine;
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

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

public sealed class CreateSenderLineTests : IAsyncLifetime
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
        await ReferenceDataTestData.EnsureDefaultsAsync(_db);
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Creates_a_sender_line_then_lists_it()
    {
        Result<CreateSenderLineResponse> created = await new CreateSenderLineHandler(_db).Handle(
            new CreateSenderLineRequest { ProviderId = 1, LineNumber = "30009999", IsSharedLine = true },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.True(created.Value.Id > 0);

        Result<IReadOnlyList<SenderLine>> listed = await new ListSenderLinesHandler(_db).Handle(CancellationToken.None);
        Assert.True(listed.IsSuccess);
        SenderLine line = Assert.Single(listed.Value, l => l.LineNumber == "30009999");
        Assert.Equal((byte)1, line.ProviderId);
        Assert.True(line.IsSharedLine);
        Assert.True(line.IsActive); // defaults to active
        Assert.False(line.UseEsb);
    }

    [Fact]
    public async Task Esb_route_is_persisted_and_can_be_updated_for_4040()
    {
        Result<CreateProviderResponse> provider = await new CreateProviderHandler(_db).Handle(
            new CreateProviderRequest { Name = "4040", Code = "4040", BaseUrl = "https://example.invalid" },
            CancellationToken.None);
        Assert.True(provider.IsSuccess, provider.Error?.Message);
        Result<CreateSenderLineResponse> created = await new CreateSenderLineHandler(_db).Handle(
            new CreateSenderLineRequest
            {
                ProviderId = provider.Value.Id, LineNumber = "40401403", IsSharedLine = true, UseEsb = true,
            }, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Result<IReadOnlyList<SenderLine>> listed = await new ListSenderLinesHandler(_db).Handle(CancellationToken.None);
        Assert.True(Assert.Single(listed.Value, line => line.Id == created.Value.Id).UseEsb);

        Result updated = await new UpdateSenderLineHandler(_db).Handle(created.Value.Id,
            new UpdateSenderLineRequest { LineNumber = "40401403", IsSharedLine = true, IsActive = true, UseEsb = false },
            CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);
        listed = await new ListSenderLinesHandler(_db).Handle(CancellationToken.None);
        Assert.False(Assert.Single(listed.Value, line => line.Id == created.Value.Id).UseEsb);
    }

    [Fact]
    public async Task Esb_cannot_be_enabled_for_a_magfa_line()
    {
        Result<CreateSenderLineResponse> result = await new CreateSenderLineHandler(_db).Handle(
            new CreateSenderLineRequest { ProviderId = 1, LineNumber = "30001111", UseEsb = true },
            CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("sender_lines.esb_provider_unsupported", result.Error!.Code);
    }

    [Fact]
    public async Task Rejects_a_line_for_an_unknown_provider()
    {
        Result<CreateSenderLineResponse> created = await new CreateSenderLineHandler(_db).Handle(
            new CreateSenderLineRequest { ProviderId = 200, LineNumber = "30001111" },
            CancellationToken.None);

        Assert.True(created.IsFailure);
        Assert.Equal(ErrorType.Validation, created.Error!.Type);
        Assert.Equal("sender_lines.unknown_provider", created.Error.Code);
    }
}
