using SmsHubNext.Shared.Sms;

namespace SmsHubNext.Features.Sending;

/// <summary>A request item paired with its resolved, frozen cost snapshot (README §6.3), ready to
/// persist. Shared by <see cref="SendMessagesHandler"/> (pricing) and <see cref="SendMessagesRowMapper"/>
/// (bulk-copy mapping).</summary>
internal sealed record PricedMessage(SendMessageItem Item, SmsSegmentInfo Segments, int TariffId, decimal UnitPrice)
{
    public decimal TotalCost => UnitPrice * Segments.SegmentCount;
}
