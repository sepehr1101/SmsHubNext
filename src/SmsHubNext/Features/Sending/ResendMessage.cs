using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

public sealed record ResendMessageRequest
{
    /// <summary>New UUID for an intentional resend; keep the same UUID when retrying an uncertain HTTP response.</summary>
    public Guid RequestId { get; init; }

    public Result Validate(long messageId) => messageId <= 0 || RequestId == Guid.Empty
        ? Error.Validation("sending.invalid_resend", "شناسه پیام و شناسه یکتای درخواست ارسال مجدد الزامی است.")
        : Result.Success();

    public static bool IsAllowed(SendStatus status, DeliveryStatus deliveryStatus) =>
        status == SendStatus.Rejected ||
        (status is SendStatus.Submitted or SendStatus.Sent &&
         deliveryStatus is DeliveryStatus.Delivered or DeliveryStatus.Undelivered or DeliveryStatus.Expired);
}

public sealed record ResendMessageResponse(long OriginalMessageId, long MessageId, long BatchId, bool IsDuplicate);
public sealed record ResendMessagePreview(long MessageId, string Recipient, SendStatus Status, DeliveryStatus DeliveryStatus, bool BodyAvailable)
{
    public bool Allowed => BodyAvailable && ResendMessageRequest.IsAllowed(Status, DeliveryStatus);
}
