namespace SmsHubNext.Features.ReferenceData;

/// <summary>A tenant — the isolation/reporting boundary (README §4.1).</summary>
public sealed record Customer(short Id, string Name, string Code, bool IsActive, DateTime CreatedAtUtc);
