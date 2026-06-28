using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// The one real abstraction seam (ARCHITECTURE.md §4): a transport that submits a single
/// message to an upstream SMS provider. Implementations return a <c>Result</c> — a failed
/// result is a transport/transient error (retry), a successful result carries the provider's
/// domain outcome (<see cref="ProviderDispatchResult"/>). Implementations never throw for
/// expected failures.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Stable provider key (matches <c>Provider.Code</c>), e.g. <c>magfa</c>.</summary>
    string Name { get; }

    Task<Result<ProviderDispatchResult>> SendAsync(ProviderSendRequest request, CancellationToken cancellationToken);
}
