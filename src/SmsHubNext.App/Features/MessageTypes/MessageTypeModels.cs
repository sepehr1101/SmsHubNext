namespace SmsHubNext.App.Features.MessageTypes;

public sealed record MessageTypeItem(byte Id, string Name, string Code, bool IsActive);
public sealed record CreateMessageTypeRequest(byte Id, string Name, string Code);
public sealed record CreateMessageTypeResponse(byte Id);
public sealed record UpdateMessageTypeRequest(string Name, bool IsActive);
