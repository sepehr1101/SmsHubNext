namespace SmsHubNext.Shared.Enums;

/// <summary>
/// Kind of money-ledger entry (README §4.15). Persisted as <c>TINYINT</c> —
/// values are stable and must not be renumbered.
/// </summary>
public enum BalanceTransactionType : byte
{
    TopUp = 1,
    Debit = 2,
    Refund = 3,
    Adjustment = 4,
}
