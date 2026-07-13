using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.GeoSections;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

public sealed class ReferenceDataMutationsTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;
    private int _actorApiKeyId;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();
        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);

        _db = new Db(connectionString);
        Result<CreateCustomerResponse> actor = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest { Name = "Reference admin", Code = $"admin-{Guid.NewGuid():N}" },
            CancellationToken.None);
        Assert.True(actor.IsSuccess);

        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = actor.Value.Id, Name = "reference-data-tests" },
            CancellationToken.None);
        Assert.True(key.IsSuccess);
        _actorApiKeyId = key.Value.Id;
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Customer_update_and_soft_delete_preserve_audit_and_block_authentication()
    {
        Result<CreateCustomerResponse> created = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest { Name = "Old customer", Code = $"customer-{Guid.NewGuid():N}" },
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        Result<IssueApiKeyResponse> targetKey = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = created.Value.Id, Name = "target" },
            CancellationToken.None);
        Assert.True(targetKey.IsSuccess);

        Result updated = await new UpdateCustomerHandler(_db).Handle(
            created.Value.Id,
            new UpdateCustomerRequest { Name = "Updated customer", Code = $"updated-{Guid.NewGuid():N}", IsActive = true },
            CancellationToken.None);
        Assert.True(updated.IsSuccess);

        Result<IReadOnlyList<Customer>> beforeDelete = await new ListCustomersHandler(_db).Handle(CancellationToken.None);
        Customer updatedCustomer = Assert.Single(beforeDelete.Value, customer => customer.Id == created.Value.Id);
        Assert.Equal("Updated customer", updatedCustomer.Name);

        Result deleted = await new DeleteCustomerHandler(_db).Handle(
            created.Value.Id, _actorApiKeyId, CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        Result<IReadOnlyList<Customer>> listed = await new ListCustomersHandler(_db).Handle(CancellationToken.None);
        Assert.DoesNotContain(listed.Value, customer => customer.Id == created.Value.Id);
        await AssertDeleteAudit("Customer", created.Value.Id);

        Result<ApiKeyIdentity> authentication = await new ApiKeyAuthenticator(_db, TimeProvider.System).Authenticate(
            targetKey.Value.Key,
            null,
            CancellationToken.None);
        Assert.True(authentication.IsFailure);
    }

    [Fact]
    public async Task Provider_update_and_soft_delete_hide_it_and_reject_new_sender_lines()
    {
        Result<CreateProviderResponse> created = await new CreateProviderHandler(_db).Handle(
            new CreateProviderRequest
            {
                Name = "Old provider",
                Code = $"provider-{Guid.NewGuid():N}",
                BaseUrl = "https://old.example.test",
            },
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        Result updated = await new UpdateProviderHandler(_db).Handle(
            created.Value.Id,
            new UpdateProviderRequest
            {
                Name = "Updated provider",
                BaseUrl = "https://new.example.test",
                FallbackBaseUrl = "https://fallback.example.test",
                IsActive = true,
            },
            CancellationToken.None);
        Assert.True(updated.IsSuccess);

        await using (SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None))
        {
            string? providerName = await connection.ExecuteScalarAsync<string>(
                "SELECT Name FROM dbo.Provider WHERE Id = @Id;",
                new { Id = created.Value.Id });
            Assert.Equal("Updated provider", providerName);
        }

        Result deleted = await new DeleteProviderHandler(_db).Handle(
            created.Value.Id, _actorApiKeyId, CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        Result<IReadOnlyList<Provider>> listed = await new ListProvidersHandler(_db).Handle(CancellationToken.None);
        Assert.DoesNotContain(listed.Value, provider => provider.Id == created.Value.Id);
        await AssertDeleteAudit("Provider", created.Value.Id);

        Result<CreateSenderLineResponse> senderLine = await new CreateSenderLineHandler(_db).Handle(
            new CreateSenderLineRequest
            {
                ProviderId = created.Value.Id,
                LineNumber = "30000001",
                IsSharedLine = true,
            },
            CancellationToken.None);
        Assert.True(senderLine.IsFailure);
        Assert.Equal("sender_lines.unknown_provider", senderLine.Error!.Code);
    }

    [Fact]
    public async Task Message_type_update_and_soft_delete_keep_stable_keys_and_hide_the_row()
    {
        byte id = 220;
        string code = $"type-{Guid.NewGuid():N}";
        Result<CreateMessageTypeResponse> created = await new CreateMessageTypeHandler(_db).Handle(
            new CreateMessageTypeRequest { Id = id, Name = "Old type", Code = code },
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        Result updated = await new UpdateMessageTypeHandler(_db).Handle(
            id,
            new UpdateMessageTypeRequest { Name = "Updated type", IsActive = true },
            CancellationToken.None);
        Assert.True(updated.IsSuccess);

        Result<IReadOnlyList<MessageType>> beforeDelete = await new ListMessageTypesHandler(_db).Handle(
            CancellationToken.None);
        MessageType row = Assert.Single(beforeDelete.Value, messageType => messageType.Id == id);
        Assert.Equal("Updated type", row.Name);
        Assert.Equal(code, row.Code);

        Result deleted = await new DeleteMessageTypeHandler(_db).Handle(
            id, _actorApiKeyId, CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        Result<IReadOnlyList<MessageType>> afterDelete = await new ListMessageTypesHandler(_db).Handle(
            CancellationToken.None);
        Assert.DoesNotContain(afterDelete.Value, messageType => messageType.Id == id);
        await AssertDeleteAudit("MessageType", id);
    }

    [Fact]
    public async Task Geo_section_requires_leaf_first_and_deleted_parent_cannot_gain_children()
    {
        CreateGeoSectionHandler create = new CreateGeoSectionHandler(_db);
        Result<CreateGeoSectionResponse> parent = await create.Handle(
            new CreateGeoSectionRequest
            {
                SectionType = GeoSectionType.Province,
                Name = "Province",
                Code = $"P-{Guid.NewGuid():N}"[..20],
            },
            CancellationToken.None);
        Assert.True(parent.IsSuccess);

        Result<CreateGeoSectionResponse> child = await create.Handle(
            new CreateGeoSectionRequest
            {
                ParentGeoSectionId = parent.Value.Id,
                SectionType = GeoSectionType.City,
                Name = "City",
                Code = $"C-{Guid.NewGuid():N}"[..20],
            },
            CancellationToken.None);
        Assert.True(child.IsSuccess);

        DeleteGeoSectionHandler delete = new DeleteGeoSectionHandler(_db);
        Result parentFirst = await delete.Handle(parent.Value.Id, _actorApiKeyId, CancellationToken.None);
        Assert.True(parentFirst.IsFailure);
        Assert.Equal(ErrorType.Conflict, parentFirst.Error!.Type);

        Result updated = await new UpdateGeoSectionHandler(_db).Handle(
            child.Value.Id,
            new UpdateGeoSectionRequest { Name = "Updated city", Code = "CITY-UPDATED", IsActive = true },
            CancellationToken.None);
        Assert.True(updated.IsSuccess);

        Result<IReadOnlyList<GeoSection>> updatedSections = await new ListGeoSectionsHandler(_db).Handle(
            CancellationToken.None);
        Assert.Contains(updatedSections.Value, section =>
            section.Id == child.Value.Id && section.Name == "Updated city" && section.Code == "CITY-UPDATED");

        Assert.True((await delete.Handle(child.Value.Id, _actorApiKeyId, CancellationToken.None)).IsSuccess);
        Assert.True((await delete.Handle(parent.Value.Id, _actorApiKeyId, CancellationToken.None)).IsSuccess);
        await AssertDeleteAudit("GeoSection", parent.Value.Id);

        Result<CreateGeoSectionResponse> rejected = await create.Handle(
            new CreateGeoSectionRequest
            {
                ParentGeoSectionId = parent.Value.Id,
                SectionType = GeoSectionType.City,
                Name = "Late city",
                Code = "LATE-CITY",
            },
            CancellationToken.None);
        Assert.True(rejected.IsFailure);
        Assert.Equal("geo.unknown_parent", rejected.Error!.Code);
    }

    [Fact]
    public async Task Sender_line_update_and_soft_delete_hide_the_row_and_reject_further_mutation()
    {
        Result<CreateSenderLineResponse> created = await new CreateSenderLineHandler(_db).Handle(
            new CreateSenderLineRequest { ProviderId = 1, LineNumber = "30000002", IsSharedLine = true },
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        Result updated = await new UpdateSenderLineHandler(_db).Handle(
            created.Value.Id,
            new UpdateSenderLineRequest { LineNumber = "30000003", IsSharedLine = true, IsActive = true },
            CancellationToken.None);
        Assert.True(updated.IsSuccess);

        Result<IReadOnlyList<SenderLine>> beforeDelete = await new ListSenderLinesHandler(_db).Handle(
            CancellationToken.None);
        Assert.Contains(beforeDelete.Value, senderLine =>
            senderLine.Id == created.Value.Id && senderLine.LineNumber == "30000003");

        Result deleted = await new DeleteSenderLineHandler(_db).Handle(
            created.Value.Id, _actorApiKeyId, CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        Result<IReadOnlyList<SenderLine>> listed = await new ListSenderLinesHandler(_db).Handle(
            CancellationToken.None);
        Assert.DoesNotContain(listed.Value, senderLine => senderLine.Id == created.Value.Id);
        await AssertDeleteAudit("SenderLine", created.Value.Id);

        Result secondUpdate = await new UpdateSenderLineHandler(_db).Handle(
            created.Value.Id,
            new UpdateSenderLineRequest { LineNumber = "30000004", IsSharedLine = true, IsActive = true },
            CancellationToken.None);
        Assert.True(secondUpdate.IsFailure);
        Assert.Equal(ErrorType.NotFound, secondUpdate.Error!.Type);
    }

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
