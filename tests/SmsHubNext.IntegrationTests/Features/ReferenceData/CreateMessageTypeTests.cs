using DbUp.Engine;
using SmsHubNext.Features.ReferenceData;
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
        // Ids 1–4 are seeded; pick a fresh stable id for the new classification.
        Result<CreateMessageTypeResponse> created = await new CreateMessageTypeHandler(_db).Handle(
            new CreateMessageTypeRequest { Id = 100, Name = "Marketing", Code = "marketing" },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal((byte)100, created.Value.Id);

        Result<IReadOnlyList<MessageType>> listed = await new ListMessageTypesHandler(_db).Handle(CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Contains(listed.Value, m => m.Id == 100 && m.Code == "marketing");
    }

    [Fact]
    public async Task Rejects_a_duplicate_code()
    {
        // "otp" is seeded at Id 1; a new id with the same code must conflict on the unique Code index.
        Result<CreateMessageTypeResponse> created = await new CreateMessageTypeHandler(_db).Handle(
            new CreateMessageTypeRequest { Id = 101, Name = "One-time password", Code = "otp" },
            CancellationToken.None);

        Assert.True(created.IsFailure);
        Assert.Equal(ErrorType.Conflict, created.Error!.Type);
        Assert.Equal("message_types.exists", created.Error.Code);
    }
}
