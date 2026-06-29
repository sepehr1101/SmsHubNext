using System.Data;
using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Sending;

/// <summary>
/// Maps priced messages to the <see cref="DataTable"/>s that <c>SqlBulkCopy</c> streams into the
/// <c>Message</c> and <c>MessageBody</c> tables (the heavy part of an accept). This SQL-shaped
/// mapping lives here rather than in <see cref="SendMessagesHandler"/> so the handler stays focused
/// on the use case; the destination table names live alongside the row shapes they belong to.
/// </summary>
internal static class SendMessagesRowMapper
{
    public const string MessageTable = "dbo.Message";
    public const string MessageBodyTable = "dbo.MessageBody";

    /// <summary>
    /// Builds the <c>Message</c> rows. Column order is independent of the destination (the bulk-copy
    /// helper maps by name and lets the server assign the identity <c>Id</c>); nullable columns carry
    /// <see cref="DBNull"/>. Column CLR types match the SQL types (TINYINT→byte, SMALLINT→short,
    /// INT→int, BIGINT→long, DECIMAL→decimal, CHAR/VARCHAR/NVARCHAR→string, DATETIME2→DateTime).
    /// </summary>
    public static DataTable BuildMessageRows(
        IReadOnlyList<PricedMessage> priced,
        SendMessagesRequest request,
        long batchId,
        byte providerId,
        short senderLineId,
        string submitDateJalali,
        DateTime submittedAtUtc)
    {
        DataTable table = new DataTable();
        table.Columns.Add("SubmitDateJalali", typeof(string));
        table.Columns.Add("MessageBatchId", typeof(long));
        table.Columns.Add("SubmittedAtUtc", typeof(DateTime));
        table.Columns.Add("CustomerId", typeof(short));
        table.Columns.Add("ProviderId", typeof(byte));
        table.Columns.Add("SenderLineId", typeof(short));
        table.Columns.Add("MessageTypeId", typeof(byte));
        table.Columns.Add("GeoSectionId", typeof(int));
        table.Columns.Add("MobileNumber", typeof(string));
        table.Columns.Add("ClientCorrelatedId", typeof(string));
        table.Columns.Add("BillId", typeof(string));
        table.Columns.Add("PayId", typeof(string));
        table.Columns.Add("Encoding", typeof(byte));
        table.Columns.Add("CharacterCount", typeof(short));
        table.Columns.Add("SegmentCount", typeof(byte));
        table.Columns.Add("TariffId", typeof(int));
        table.Columns.Add("UnitPrice", typeof(decimal));
        table.Columns.Add("TotalCost", typeof(decimal));
        table.Columns.Add("Status", typeof(byte));
        table.Columns.Add("DeliveryStatus", typeof(byte));

        foreach (PricedMessage message in priced)
        {
            table.Rows.Add(
                submitDateJalali,
                batchId,
                submittedAtUtc,
                request.CustomerId,
                providerId,
                senderLineId,
                request.MessageTypeId,
                (object?)message.Item.GeoSectionId ?? DBNull.Value,
                message.Item.Recipient,
                (object?)message.Item.ClientCorrelatedId ?? DBNull.Value,
                (object?)message.Item.BillId ?? DBNull.Value,
                (object?)message.Item.PayId ?? DBNull.Value,
                (byte)message.Segments.Encoding,
                (short)message.Segments.CharacterCount,
                (byte)message.Segments.SegmentCount,
                message.TariffId,
                message.UnitPrice,
                message.TotalCost,
                (byte)SendStatus.Queued,
                (byte)DeliveryStatus.Pending);
        }

        return table;
    }

    /// <summary>Builds the 1:1 <c>MessageBody</c> rows, pairing each read-back id with its body
    /// (both in insertion order — see <see cref="SendingSql.SelectBatchMessageIds"/>).</summary>
    public static DataTable BuildBodyRows(IReadOnlyList<long> messageIds, IReadOnlyList<PricedMessage> priced)
    {
        DataTable table = new DataTable();
        table.Columns.Add("Id", typeof(long));
        table.Columns.Add("Body", typeof(string));

        for (int i = 0; i < messageIds.Count; i++)
            table.Rows.Add(messageIds[i], priced[i].Item.Text);

        return table;
    }
}
