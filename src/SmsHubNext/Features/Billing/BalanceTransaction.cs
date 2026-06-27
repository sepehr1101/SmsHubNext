using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Billing;

/// <summary>An entry in the append-only money ledger (README §4.15).</summary>
public sealed record BalanceTransaction(
    long Id,
    short CustomerId,
    BalanceTransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    long? MessageBatchId,
    string? Reference,
    DateTime CreatedAtUtc);
