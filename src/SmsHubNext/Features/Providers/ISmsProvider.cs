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

    Task<Result<ProviderDispatchResult>> SendAsync(ProviderSendRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Queries the provider for the delivery status of previously-submitted messages, by their
    /// provider message ids (the DLR-polling pipeline, Phase 2). A failed <c>Result</c> is a
    /// transport/transient error (retry next cycle); a successful result carries one
    /// <see cref="ProviderDeliveryReport"/> per id the provider returned a status for.
    /// </summary>
    Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken);
}
