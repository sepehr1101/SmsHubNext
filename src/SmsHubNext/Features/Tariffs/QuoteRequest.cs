using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

public sealed class QuoteRequest
{
    public byte ProviderId { get; init; }
    public byte? MessageTypeId { get; init; }
    public string Text { get; init; } = string.Empty;

    public Result Validate()
    {
        if (ProviderId == 0)
            return Error.Validation("tariffs.provider_required", "A provider id is required.");

        if (string.IsNullOrWhiteSpace(Text))
            return Error.Validation("tariffs.text_required", "Message text is required.");

        return Result.Success();
    }
}
