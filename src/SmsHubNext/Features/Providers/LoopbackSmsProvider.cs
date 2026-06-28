using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// A stand-in provider that accepts every message and reports it delivered. It lets the full
/// accept → dispatch → delivery-report pipeline run end-to-end without the real Magfa client
/// (or credentials). The Magfa <see cref="ISmsProvider"/> replaces this as the default
/// registration when enabled; this stays useful for local/dev runs.
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

    public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds,
        CancellationToken cancellationToken)
    {
        // Every queried message is reported delivered, so the dev pipeline reaches a terminal state.
        IReadOnlyList<ProviderDeliveryReport> reports = providerMessageIds
            .Select(id => new ProviderDeliveryReport(id, DeliveryReportStatus.Delivered, RawStatusCode: 1))
            .ToList();
        return Task.FromResult(Result.Success(reports));
    }
}
