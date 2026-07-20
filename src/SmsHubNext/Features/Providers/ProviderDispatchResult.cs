namespace SmsHubNext.Features.Providers;

/// <summary>
/// The domain outcome of handing one message to a provider (distinct from a transport
/// failure, which is a failed <c>Result</c>):
/// <list type="bullet">
/// <item><see cref="Accepted"/> — the provider took the message; <c>ProviderMessageId</c> is the DLR-matching key.</item>
/// <item><see cref="Rejected"/> — the provider refused this message at submission; it was never sent (⇒ refund).</item>
/// <item><see cref="InsufficientCredit"/> — the provider account is out of credit; the batch is held and resumed later.</item>
/// <item><see cref="DefinitelyNotSubmitted"/> — an authentication/account failure proves the
/// provider accepted no message in the request (⇒ fail and refund the batch).</item>
/// <item><see cref="RetryableNotSubmitted"/> — an explicit transient response proves the message
/// was not accepted and may be retried without confirmation lookup.</item>
/// </list>
/// </summary>
public enum ProviderDispatchStatus
{
    Accepted,
    Rejected,
    InsufficientCredit,
    DefinitelyNotSubmitted,
    RetryableNotSubmitted,
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

    public static ProviderDispatchResult DefinitelyNotSubmitted(int? resultCode, string? detail = null) =>
        new(ProviderDispatchStatus.DefinitelyNotSubmitted, null, resultCode, detail);

    public static ProviderDispatchResult RetryableNotSubmitted(int? resultCode, string? detail = null) =>
        new(ProviderDispatchStatus.RetryableNotSubmitted, null, resultCode, detail);
}
