using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Landing;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Landing;

public sealed class FaqTests : IAsyncLifetime
{
    private readonly MsSqlContainer _server = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;
    private int _actor;
    public async Task InitializeAsync()
    {
        await _server.StartAsync();
        _db = new Db(_server.GetConnectionString());
        DatabaseUpgradeResult migration = new DatabaseMigrator(_server.GetConnectionString()).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        _actor = await connection.QuerySingleAsync<int>("""
            INSERT dbo.Customer (Name, Code) VALUES (N'FAQ admin', 'faq-test');
            DECLARE @CustomerId SMALLINT = SCOPE_IDENTITY();
            INSERT dbo.ApiKey (CustomerId, Name, KeyPrefix, KeyHash, AccessLevel)
            OUTPUT inserted.Id VALUES (@CustomerId, N'FAQ admin', 'faq', HASHBYTES('SHA2_256', 'faq-test'), 1);
            """);
    }
    public Task DisposeAsync() => _server.DisposeAsync().AsTask();

    [Fact]
    public async Task Persists_edits_order_and_deletion_for_public_read()
    {
        FaqHandler handler = new(_db);
        Assert.Equal(3, (await handler.List(default)).Value.Count);
        Assert.True((await handler.Save(null, new() { Question = "سؤال جدید", Answer = "پاسخ جدید" }, _actor, default)).IsSuccess);
        FaqItem created = (await handler.List(default)).Value.Last();
        Assert.True((await handler.Move(created.Id, -1, _actor, default)).IsSuccess);
        Assert.Equal(created.Id, (await handler.List(default)).Value[2].Id);
        Assert.True((await handler.Save(created.Id, new() { Question = "ویرایش", Answer = "پاسخ ویرایش" }, _actor, default)).IsSuccess);
        Assert.Equal("ویرایش", (await handler.List(default)).Value[2].Question);
        Assert.True((await handler.Delete(created.Id, default)).IsSuccess);
        Assert.Equal(3, (await handler.List(default)).Value.Count);
        Assert.True((await handler.Move(created.Id, 1, _actor, default)).IsFailure);
    }
}
