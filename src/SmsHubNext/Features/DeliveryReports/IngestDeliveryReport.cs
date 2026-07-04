using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DeliveryReports;

/// <summary>
/// A delivery report (DLR) to ingest for a message: the provider's status, already
/// normalized to our vocabulary, plus its raw native code.
///
/// INTERIM: matches the message by our own <see cref="MessageId"/>. Once provider
/// dispatch stamps <c>Message.ProviderMessageId</c>, real provider callbacks will be
/// matched by <c>(ProviderId, ProviderMessageId)</c> instead (README §4.10, NCIX 2).
/// </summary>
public sealed class IngestDeliveryReportRequest
{
    public long MessageId { get; init; }

    /// <summary>The provider's outcome, normalized to our vocabulary.</summary>
    public DeliveryReportStatus Status { get; init; }

    /// <summary>The provider-native status code, kept verbatim for forensics.</summary>
    public int RawStatusCode { get; init; }

    public Result Validate()
    {
        if (MessageId <= 0)
            return Error.Validation("delivery_reports.message_required", "A valid message id is required.");

        if (!Enum.IsDefined(Status))
            return Error.Validation("delivery_reports.invalid_status", "The normalized status is not recognized.");

        return Result.Success();
    }
}

/// <summary>
/// Acknowledgement of an ingested report: the new <c>DeliveryReport</c> id and the
/// resulting denormalized <see cref="DeliveryStatus"/> read model on the message.
/// </summary>
public sealed record IngestDeliveryReportResponse(long ReportId, DeliveryStatus DeliveryStatus, bool AppliedToReadModel);
