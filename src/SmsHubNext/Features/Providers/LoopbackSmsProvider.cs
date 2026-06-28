using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// A stand-in provider that accepts every message and returns a synthetic provider
/// message id. It lets the full accept → dispatch → status pipeline run end-to-end
/// before the real Magfa client exists (roadmap Phase 1). The Magfa <see cref="ISmsProvider"/>
/// replaces this as the default registration; this stays useful for local/dev runs.
/// </summary>
public sealed class LoopbackSmsProvider : ISmsProvider
{
    public string Name => "loopback";

    public Task<Result<ProviderDispatchResult>> SendAsync(
        ProviderSendRequest request,
        CancellationToken cancellationToken)
    {
        // A unique, provider-shaped id so DLR matching has something to key on later.
        string providerMessageId = Guid.NewGuid().ToString("N");
        return Task.FromResult(Result.Success(ProviderDispatchResult.Accepted(providerMessageId)));
    }
}
