namespace SmsHubNext.Features.ReferenceData.SenderLines;

/// <summary>A sending line belonging to a provider (README §4.5).</summary>
public sealed record SenderLine(
    short Id,
    byte ProviderId,
    string LineNumber,
    bool IsSharedLine,
    short? CustomerId,
    bool IsActive);
