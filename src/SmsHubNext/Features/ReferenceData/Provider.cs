namespace SmsHubNext.Features.ReferenceData;

/// <summary>An SMS provider (README §4.4). Endpoints/credentials are intentionally omitted here.</summary>
public sealed record Provider(byte Id, string Name, string Code, bool IsActive);
