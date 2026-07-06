namespace SmsHubNext.Features.Sending;

internal sealed class SenderLineRow
{
    public SenderLineRow()
    {
    }

    public short Id { get; set; }
    public byte ProviderId { get; set; }
    public bool IsSharedLine { get; set; }
    public short? CustomerId { get; set; }
    public bool IsActive { get; set; }
    public int? ProviderAccountId { get; set; }
    public bool? ProviderAccountIsActive { get; set; }
    public long? SecretLength { get; set; }
}
