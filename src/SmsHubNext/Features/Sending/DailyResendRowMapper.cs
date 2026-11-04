using System.Data;

namespace SmsHubNext.Features.Sending;

internal sealed record DailyPlannedItem(Guid OperationId, long SourceMessageId, Guid RequestId, decimal? EstimatedCost, byte State, string? SkipCode);

internal static class DailyResendRowMapper
{
    public const string Table = "dbo.DailyResendItem";

    public static DataTable Build(IReadOnlyList<DailyPlannedItem> items)
    {
        DataTable rows = new();
        rows.Columns.Add("OperationId", typeof(Guid));
        rows.Columns.Add("SourceMessageId", typeof(long));
        rows.Columns.Add("RequestId", typeof(Guid));
        rows.Columns.Add("EstimatedCost", typeof(decimal));
        rows.Columns.Add("State", typeof(byte));
        rows.Columns.Add("SkipCode", typeof(string));
        foreach (DailyPlannedItem item in items)
            rows.Rows.Add(item.OperationId, item.SourceMessageId, item.RequestId,
                item.EstimatedCost.HasValue ? item.EstimatedCost.Value : DBNull.Value, item.State,
                item.SkipCode is null ? DBNull.Value : item.SkipCode);
        return rows;
    }
}
