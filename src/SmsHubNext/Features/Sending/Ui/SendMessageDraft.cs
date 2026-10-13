namespace SmsHubNext.Features.Sending.Ui;

public sealed class SendMessageDraft
{
    public string Recipient { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ClientCorrelatedId { get; set; }
    public string? BillId { get; set; }
    public string? PayId { get; set; }
    public int? GeoSectionId { get; set; }
}
