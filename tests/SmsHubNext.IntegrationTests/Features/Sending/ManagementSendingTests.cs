using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Sending;

public sealed partial class SendMessagesTests
{
    [Fact]
    public async Task Management_send_uses_selected_line_owner_and_real_admin_and_accepts_duplicate_only_once()
    {
        (long sourceId, ApiKeyIdentity owner) = await CreateResendSource();
        short otherCustomer = await CreateCustomerAsync("other-ui-customer");
        ApiKeyIdentity otherKey = await IssueApiKeyAsync(otherCustomer);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.SenderLine SET IsSharedLine=0, CustomerId=@Id WHERE LineNumber='30001234'", new { Id = owner.CustomerId });
        SmsHubNext.Deployment.SetupAdministratorCredential credential = await new SmsHubNext.Deployment.AdminApiKeyBootstrapper(_db)
            .CreateAsync("manual-admin", null, CancellationToken.None);
        ApiKeyIdentity admin = (await new ApiKeyAuthenticator(_db, TimeProvider.System)
            .Authenticate(credential.ApiKey, System.Net.IPAddress.Loopback, CancellationToken.None)).Value;
        SendMessagesHandler handler = new(_db, TimeProvider.System);
        SendMessagesRequest request = new()
        {
            SenderLine = "30001234", MessageTypeId = 1, ClientBatchId = "manual-owner",
            Messages = [new() { Recipient = "989120000001", Text = "Manual test" }],
        };
        Result<SendMessagesResponse> oldPath = await handler.Handle(request, otherKey, CancellationToken.None);
        Assert.Equal("sending.sender_line_not_allowed", oldPath.Error?.Code);
        Result<SendMessagesResponse> wrongOwner = await handler.HandleManagement(request, admin, otherCustomer, CancellationToken.None);
        Assert.Equal("sending.sender_line_not_allowed", wrongOwner.Error?.Code);
        Result<SendMessagesResponse> unauthorized = await handler.HandleManagement(request, otherKey, owner.CustomerId, CancellationToken.None);
        Assert.Equal("sending.administrator_required", unauthorized.Error?.Code);
        decimal before = await connection.ExecuteScalarAsync<decimal>("SELECT Balance FROM dbo.CustomerBalance WHERE CustomerId=@Id", new { Id = owner.CustomerId });
        Result<SendMessagesResponse>[] sends = await Task.WhenAll(
            handler.HandleManagement(request, admin, owner.CustomerId, CancellationToken.None),
            handler.HandleManagement(request, admin, owner.CustomerId, CancellationToken.None));
        Assert.All(sends, result => Assert.True(result.IsSuccess, result.Error?.Message));
        Assert.Single(sends, result => !result.Value.IsDuplicate);
        Assert.Single(sends, result => result.Value.IsDuplicate);
        long batchId = sends[0].Value.BatchId;
        Assert.Equal(batchId, sends[1].Value.BatchId);
        (short CustomerId, int ApiKeyId, decimal TotalCost) batch = await connection.QuerySingleAsync<(short, int, decimal)>(
            "SELECT CustomerId,ApiKeyId,TotalCost FROM dbo.MessageBatch WHERE Id=@Id", new { Id = batchId });
        Assert.Equal(owner.CustomerId, batch.CustomerId);
        Assert.Equal(credential.Id, batch.ApiKeyId);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE MessageBatchId=@Id", new { Id = batchId }));
        Assert.Equal(before - batch.TotalCost, await connection.ExecuteScalarAsync<decimal>("SELECT Balance FROM dbo.CustomerBalance WHERE CustomerId=@Id", new { Id = owner.CustomerId }));
    }
}
