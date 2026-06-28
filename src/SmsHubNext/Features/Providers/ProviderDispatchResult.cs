namespace SmsHubNext.Features.Providers;

/// <summary>
/// The domain outcome of handing one message to a provider (distinct from a transport
/// failure, which is a failed <c>Result</c>):
/// <list type="bullet">
/// <item><see cref="Accepted"/> — the provider took the message; <c>ProviderMessageId</c> is the DLR-matching key.</item>
/// <item><see cref="Rejected"/> — the provider refused this message at submission; it was never sent (⇒ refund).</item>
/// <item><see cref="InsufficientCredit"/> — the provider account is out of credit; the batch is held and resumed later.</item>
/// </list>
/// </summary>
public enum ProviderDispatchStatus
{
    Accepted,
    Rejected,
    InsufficientCredit,
}

/// <summary>
/// The result of a provider submission. A transport/transient failure is modelled as a
/// failed <c>Result&lt;ProviderDispatchResult&gt;</c> (ARCHITECTURE.md §6); this type carries
/// only the domain outcomes the dispatcher acts on.
/// </summary>
public sealed record ProviderDispatchResult(
    ProviderDispatchStatus Status,
    string? ProviderMessageId,
    int? ProviderResultCode,
    string? Detail)
{
    public static ProviderDispatchResult Accepted(string providerMessageId, int? resultCode = null) =>
        new(ProviderDispatchStatus.Accepted, providerMessageId, resultCode, null);

    public static ProviderDispatchResult Rejected(int? resultCode, string? detail = null) =>
        new(ProviderDispatchStatus.Rejected, null, resultCode, detail);

    public static ProviderDispatchResult InsufficientCredit(int? resultCode = null, string? detail = null) =>
        new(ProviderDispatchStatus.InsufficientCredit, null, resultCode, detail);
}
