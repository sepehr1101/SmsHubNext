namespace SmsHubNext.App.Features.Sending;

public enum QuoteSmsEncoding : byte { Gsm7 = 1, Unicode = 2 }
public sealed record QuoteRequest(byte ProviderId, byte? MessageTypeId, string Text);
public sealed record CostQuote(QuoteSmsEncoding Encoding, int CharacterCount, int SegmentCount, int TariffId, decimal UnitPrice, decimal TotalCost, string Currency);
public sealed record SendMessageItem(string Recipient, string Text, string? ClientCorrelatedId, string? BillId, string? PayId, int? GeoSectionId);
public sealed record SendMessagesRequest(short CustomerId, string SenderLine, byte MessageTypeId, string ClientBatchId, IReadOnlyList<SendMessageItem> Messages);
public sealed record SendMessagesResponse(long BatchId, int AcceptedCount, bool IsDuplicate);
public sealed class SendMessageDraft
{
    public string Recipient { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ClientCorrelatedId { get; set; }
    public string? BillId { get; set; }
    public string? PayId { get; set; }
    public int? GeoSectionId { get; set; }
}
