using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Enums;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Sending;

public sealed class ResendMessageTests
{
    [Fact]
    public void Requires_a_message_and_nonempty_request_uuid()
    {
        Assert.True(new ResendMessageRequest().Validate(1).IsFailure);
        Assert.True(new ResendMessageRequest { RequestId = Guid.NewGuid() }.Validate(0).IsFailure);
        Assert.True(new ResendMessageRequest { RequestId = Guid.NewGuid() }.Validate(1).IsSuccess);
    }

    [Fact]
    public void Only_definite_send_outcomes_allow_an_intentional_resend()
    {
        foreach (SendStatus status in Enum.GetValues<SendStatus>())
        foreach (DeliveryStatus delivery in Enum.GetValues<DeliveryStatus>())
        {
            bool expected = status == SendStatus.Rejected ||
                ((status == SendStatus.Submitted || status == SendStatus.Sent) &&
                 (delivery == DeliveryStatus.Delivered || delivery == DeliveryStatus.Undelivered || delivery == DeliveryStatus.Expired));
            Assert.Equal(expected, ResendMessageRequest.IsAllowed(status, delivery));
        }
        Assert.False(new ResendMessagePreview(1, "989120000001", SendStatus.Rejected, DeliveryStatus.Pending, false).Allowed);
    }
}
