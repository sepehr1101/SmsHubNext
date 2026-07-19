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

public sealed class CreateMessageTypeTests : IAsyncLifetime
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
    public async Task Creates_a_message_type_then_lists_it()
    {
        Result<CreateMessageTypeResponse> created = await new CreateMessageTypeHandler(_db).Handle(
            new CreateMessageTypeRequest { Id = 1, Name = "Marketing", Code = "marketing" },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal((byte)1, created.Value.Id);

        Result<IReadOnlyList<MessageType>> listed = await new ListMessageTypesHandler(_db).Handle(CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Contains(listed.Value, m => m.Id == 1 && m.Code == "marketing");
    }

    [Fact]
    public async Task Rejects_a_duplicate_code()
    {
        Result<CreateMessageTypeResponse> first = await new CreateMessageTypeHandler(_db).Handle(
            new CreateMessageTypeRequest { Id = 1, Name = "One-time password", Code = "otp" },
            CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error?.Message);

        Result<CreateMessageTypeResponse> created = await new CreateMessageTypeHandler(_db).Handle(
            new CreateMessageTypeRequest { Id = 2, Name = "Duplicate OTP", Code = "otp" },
            CancellationToken.None);

        Assert.True(created.IsFailure);
        Assert.Equal(ErrorType.Conflict, created.Error!.Type);
        Assert.Equal("message_types.exists", created.Error.Code);
    }
}
