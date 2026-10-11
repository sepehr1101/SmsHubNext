namespace SmsHubNext.App.Features.Billing;
public enum BalanceTransactionType : byte { TopUp = 1, Debit = 2, Refund = 3, Adjustment = 4 }
public sealed record CustomerBalanceItem(short CustomerId, decimal Balance, DateTime UpdatedAtUtc);
public sealed record BalanceTransactionItem(long Id, short CustomerId, BalanceTransactionType Type, decimal Amount, decimal BalanceAfter, long? MessageBatchId, string? Reference, DateTime CreatedAtUtc);
public sealed record TopUpRequest(short CustomerId, decimal Amount, string? Reference);
public sealed record TopUpResponse(short CustomerId, decimal Balance, bool IsDuplicate = false);
