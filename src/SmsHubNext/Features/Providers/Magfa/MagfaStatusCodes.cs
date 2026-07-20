namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>How a Magfa status code (API reference §8) maps onto the dispatcher's outcomes.</summary>
public enum MagfaDisposition
{
    /// <summary>Status <c>0</c> — message accepted.</summary>
    Accepted,

    /// <summary>Status <c>14</c> — provider account out of credit; the batch is held and resumed later.</summary>
    InsufficientCredit,

    /// <summary>A permanent per-message refusal; the message was never sent (⇒ refund).</summary>
    Rejected,

    /// <summary>A transport/transient condition (server busy, no capacity); the batch is re-queued and retried.</summary>
    Transient,

    /// <summary>A configuration/auth/protocol fault (bad credentials, IP not allowed, malformed request).</summary>
    Fatal,
}

/// <summary>
/// Classifies Magfa status codes into <see cref="MagfaDisposition"/>. Keeping the whole table in
/// one place (rather than scattering <c>switch</c> arms through the provider) makes the policy
/// auditable against the reference doc. Codes not in a list fall through to <see cref="MagfaDisposition.Fatal"/>:
/// an unknown code is safer surfaced loudly than silently retried or refunded.
/// </summary>
public static class MagfaStatusCodes
{
    public const int Success = 0;
    public const int InsufficientCredit = 14;

    /// <summary>Permanent per-message refusals: the recipient/message can never be delivered as-is.</summary>
    private static readonly HashSet<int> Rejected = [1, 8, 13, 20, 27, 28, 30, 33, 34, 35];

    /// <summary>Transient: worth retrying the same request later.</summary>
    private static readonly HashSet<int> Transient = [15, 23];

    /// <summary>Request-level account/authentication failures that prove no message was accepted.</summary>
    private static readonly HashSet<int> DefinitelyNotSubmitted = [16, 17, 18, 19, 22, 29];

    public static bool IsDefinitelyNotSubmitted(int status) => DefinitelyNotSubmitted.Contains(status);

    public static MagfaDisposition Classify(int status) => status switch
    {
        Success => MagfaDisposition.Accepted,
        InsufficientCredit => MagfaDisposition.InsufficientCredit,
        _ when Rejected.Contains(status) => MagfaDisposition.Rejected,
        _ when Transient.Contains(status) => MagfaDisposition.Transient,
        _ => MagfaDisposition.Fatal,
    };
}
