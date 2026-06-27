namespace SmsHubNext.Features.Billing;

/// <summary>A customer's current prepaid balance (README §4.14).</summary>
public sealed record CustomerBalance(short CustomerId, decimal Balance, DateTime UpdatedAtUtc);
