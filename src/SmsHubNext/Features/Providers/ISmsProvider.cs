using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// The one real abstraction seam (ARCHITECTURE.md §4): a transport to an upstream SMS provider.
/// It both submits messages and polls their delivery status. Implementations return a
/// <c>Result</c> — a failed result is a transport/transient error (retry), a successful result
/// carries the provider's domain outcome. Implementations never throw for expected failures.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Stable provider key (matches <c>Provider.Code</c>), e.g. <c>magfa</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The most messages this provider accepts in one <see cref="SendBatchAsync"/> request (Magfa: 100).
    /// The dispatcher chunks a batch's queued messages by this size — one HTTP request per chunk.
    /// </summary>
    int MaxBatchSize { get; }

    /// <summary>
    /// Submits up to <see cref="MaxBatchSize"/> messages in one provider request. The two result
    /// levels are deliberately distinct so batching changes the transport only, never the per-message
    /// domain outcome:
    /// <list type="bullet">
    /// <item>A failed <b>outer</b> <c>Result</c> is a whole-request transport/transient failure — none
    /// of the messages were taken; the dispatcher re-queues the chunk and retries later.</item>
    /// <item>A successful outer result carries exactly one <b>inner</b>
    /// <c>Result&lt;ProviderDispatchResult&gt;</c> per input request, <b>in input order</b>. A failed
    /// inner result is that one message's transient failure (leave it queued, retry just it); a
    /// successful inner result is its domain outcome (accepted / rejected / out-of-credit).</item>
    /// </list>
    /// Implementations never throw for expected failures and always return one inner result per request.
    /// </summary>
    Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken);

    /// <summary>
    /// Queries the provider for the delivery status of previously-submitted messages, by their
    /// provider message ids (the DLR-polling pipeline, Phase 2). A failed <c>Result</c> is a
    /// transport/transient error (retry next cycle); a successful result carries one
    /// <see cref="ProviderDeliveryReport"/> per id the provider returned a status for.
    /// </summary>
    Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken);
}
