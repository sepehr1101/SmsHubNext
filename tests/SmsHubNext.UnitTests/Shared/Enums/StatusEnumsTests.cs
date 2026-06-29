using SmsHubNext.Shared.Enums;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Enums;

public class StatusEnumsTests
{
    // Persisted (TINYINT) values must stay stable — guard against accidental renumbering.

    [Theory]
    [InlineData(SendStatus.Queued, 1)]
    [InlineData(SendStatus.Submitted, 2)]
    [InlineData(SendStatus.Sent, 3)]
    [InlineData(SendStatus.Rejected, 4)]
    [InlineData(SendStatus.Unknown, 5)]
    [InlineData(SendStatus.AwaitingConfirmation, 6)]
    public void SendStatus_values_are_stable(SendStatus status, byte expected)
        => Assert.Equal(expected, (byte)status);

    [Theory]
    [InlineData(DeliveryStatus.Pending, 1)]
    [InlineData(DeliveryStatus.Delivered, 2)]
    [InlineData(DeliveryStatus.Undelivered, 3)]
    [InlineData(DeliveryStatus.Expired, 4)]
    [InlineData(DeliveryStatus.Unknown, 5)]
    public void DeliveryStatus_values_are_stable(DeliveryStatus status, byte expected)
        => Assert.Equal(expected, (byte)status);

    [Theory]
    [InlineData(DeliveryReportStatus.Delivered, 1)]
    [InlineData(DeliveryReportStatus.Undelivered, 2)]
    [InlineData(DeliveryReportStatus.Expired, 3)]
    [InlineData(DeliveryReportStatus.Rejected, 4)]
    [InlineData(DeliveryReportStatus.Unknown, 5)]
    public void DeliveryReportStatus_values_are_stable(DeliveryReportStatus status, byte expected)
        => Assert.Equal(expected, (byte)status);

    [Theory]
    [InlineData(BatchStatus.Received, 1)]
    [InlineData(BatchStatus.Dispatching, 2)]
    [InlineData(BatchStatus.Completed, 3)]
    [InlineData(BatchStatus.PartiallyFailed, 4)]
    [InlineData(BatchStatus.Held, 5)]
    [InlineData(BatchStatus.Rejected, 6)]
    [InlineData(BatchStatus.Failed, 7)]
    public void BatchStatus_values_are_stable(BatchStatus status, byte expected)
        => Assert.Equal(expected, (byte)status);

    [Theory]
    [InlineData(BatchStatusReason.InsufficientProviderCredit, 1)]
    [InlineData(BatchStatusReason.InsufficientCustomerBalance, 2)]
    public void BatchStatusReason_values_are_stable(BatchStatusReason reason, byte expected)
        => Assert.Equal(expected, (byte)reason);

    [Theory]
    [InlineData(DeliveryStatus.Pending, false)]
    [InlineData(DeliveryStatus.Delivered, true)]
    [InlineData(DeliveryStatus.Undelivered, true)]
    [InlineData(DeliveryStatus.Expired, true)]
    [InlineData(DeliveryStatus.Unknown, true)]
    public void DeliveryStatus_terminal_classification(DeliveryStatus status, bool isTerminal)
        => Assert.Equal(isTerminal, status.IsTerminal());

    [Theory]
    [InlineData(BatchStatus.Received, false)]
    [InlineData(BatchStatus.Dispatching, false)]
    [InlineData(BatchStatus.Held, false)]
    [InlineData(BatchStatus.Completed, true)]
    [InlineData(BatchStatus.PartiallyFailed, true)]
    [InlineData(BatchStatus.Rejected, true)]
    [InlineData(BatchStatus.Failed, true)]
    public void BatchStatus_terminal_classification(BatchStatus status, bool isTerminal)
        => Assert.Equal(isTerminal, status.IsTerminal());
}
