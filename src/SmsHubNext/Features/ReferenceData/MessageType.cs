namespace SmsHubNext.Features.ReferenceData;

/// <summary>A message classification — delivery class merged with business purpose (README §4.6).</summary>
public sealed record MessageType(byte Id, string Name, string Code);
