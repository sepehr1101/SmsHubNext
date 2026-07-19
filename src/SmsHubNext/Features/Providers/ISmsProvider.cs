using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// The one real abstraction seam (ARCHITECTURE.md): a transport to an upstream SMS provider.
/// It submits messages, reconciles uncertain submissions, polls delivery status, and fetches inbound
/// messages. Implementations return a <c>Result</c> and never throw for expected provider failures.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Stable provider key matching <c>Provider.Code</c>, for example <c>magfa</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The most messages this provider accepts in one <see cref="SendBatchAsync"/> request.
    /// </summary>
    int MaxBatchSize { get; }

    /// <summary>
    /// Whether resubmitting the same <see cref="ProviderSendRequest.MessageId"/> is guaranteed by
    /// the provider not to create a second SMS. Providers without this guarantee must be held for
    /// manual review after an unknown submit outcome cannot be positively confirmed.
    /// </summary>
    bool SupportsIdempotentResend { get; }

    /// <summary>
    /// Submits up to <see cref="MaxBatchSize"/> messages in one provider request.
    /// A failed outer <c>Result</c> means the provider outcome is unknown; the dispatcher must keep
    /// those messages in <c>AwaitingConfirmation</c> and reconcile by uid before any resend.
    /// A successful outer result carries one inner result per input request, in input order.
    /// </summary>
    Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken);

    /// <summary>
    /// Recovers from a send whose response was lost: looks up whether the provider already accepted
    /// the message submitted with this id as correlation id / uid. Returns the provider message id if
    /// it was accepted, or <c>null</c> if the provider has no record of it.
    /// </summary>
    Task<Result<string?>> ResolveSubmittedMessageIdAsync(long messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Queries the provider for delivery status by provider message id.
    /// </summary>
    Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken);

    /// <summary>
    /// Pulls up to <paramref name="maxCount"/> inbound messages from the provider inbox.
    /// </summary>
    Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
        int maxCount, CancellationToken cancellationToken);
}
