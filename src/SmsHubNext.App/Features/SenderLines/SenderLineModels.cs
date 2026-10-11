namespace SmsHubNext.App.Features.SenderLines;

public sealed record SenderLineItem(short Id, byte ProviderId, string LineNumber, bool IsSharedLine, short? CustomerId, int? ProviderAccountId, bool IsActive);
public sealed record CreateSenderLineRequest(byte ProviderId, string LineNumber, bool IsSharedLine, short? CustomerId, int? ProviderAccountId, bool IsActive);
public sealed record CreateSenderLineResponse(short Id);
public sealed record UpdateSenderLineRequest(string LineNumber, bool IsSharedLine, short? CustomerId, int? ProviderAccountId, bool IsActive);
