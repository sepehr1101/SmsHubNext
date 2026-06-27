namespace SmsHubNext.Features.Billing;

public sealed record TopUpResponse(short CustomerId, decimal Balance);
