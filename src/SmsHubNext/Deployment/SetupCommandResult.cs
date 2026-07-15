namespace SmsHubNext.Deployment;

public sealed record SetupCommandResult(bool Success, string Code, string Message)
{
    public static SetupCommandResult Succeeded(string code, string message) => new(true, code, message);

    public static SetupCommandResult Failed(string code, string message) => new(false, code, message);
}
