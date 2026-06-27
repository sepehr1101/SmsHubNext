namespace SmsHubNext.Shared.Enums;

/// <summary>
/// Why a <c>MessageBatch</c> is in its current state — the nullable
/// <c>StatusReason</c> column (README §4.13). Persisted as <c>TINYINT</c>;
/// the column is null when no reason applies. Values are stable.
/// </summary>
public enum BatchStatusReason : byte
{
    InsufficientProviderCredit = 1,
    InsufficientCustomerBalance = 2,
}
