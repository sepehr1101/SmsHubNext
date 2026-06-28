using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DeliveryReports;

/// <summary>Lists a message's full status-event history, newest first (README §4.12).</summary>
public sealed class ListDeliveryReportsHandler
{
    private readonly Db _db;

    public ListDeliveryReportsHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<DeliveryReport>>> Handle(long messageId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        IEnumerable<DeliveryReport> rows = await connection.QueryAsync<DeliveryReport>(new CommandDefinition(
            DeliveryReportsSql.ListByMessage,
            new { MessageId = messageId },
            cancellationToken: cancellationToken));

        IReadOnlyList<DeliveryReport> reports = rows.AsList();
        return Result.Success(reports);
    }
}
