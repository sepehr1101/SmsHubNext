namespace SmsHubNext.App.Features.Customers;

public sealed record CustomerItem(short Id, string Name, string Code, bool IsActive, DateTime CreatedAtUtc);
public sealed record CreateCustomerRequest(string Name, string Code);
public sealed record CreateCustomerResponse(short Id);
public sealed record UpdateCustomerRequest(string Name, string Code, bool IsActive);
