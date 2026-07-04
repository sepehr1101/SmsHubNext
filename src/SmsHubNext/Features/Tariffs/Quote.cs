using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;

namespace SmsHubNext.Features.Tariffs;

public sealed class QuoteRequest
{
    public byte ProviderId { get; init; }
    public byte? MessageTypeId { get; init; }
    public string Text { get; init; } = string.Empty;

    public Result Validate()
    {
        if (ProviderId == 0)
            return Error.Validation("tariffs.provider_required", UserMessages.Tariffs.ProviderRequired);

        if (string.IsNullOrWhiteSpace(Text))
            return Error.Validation("tariffs.text_required", UserMessages.Tariffs.TextRequired);

        return Result.Success();
    }
}

/// <summary>
/// The resolved price for a message — exactly the values frozen onto the
/// <c>Message</c> at submission (README §6.3).
/// </summary>
public sealed record CostQuote(
    SmsEncoding Encoding,
    int CharacterCount,
    int SegmentCount,
    int TariffId,
    decimal UnitPrice,
    decimal TotalCost,
    string Currency);
