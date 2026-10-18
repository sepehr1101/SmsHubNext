using System.Text.Json.Serialization;

namespace SmsHubNext.Deployment;

public sealed record SetupCommandResult(
    bool Success,
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SetupAdministratorCredential? Administrator = null)
{
    public static SetupCommandResult Succeeded(string code, string message) => new(true, code, message);

    public static SetupCommandResult Succeeded(
        string code,
        string message,
        SetupAdministratorCredential administrator) =>
        new(true, code, message, administrator);

    public static SetupCommandResult Failed(string code, string message) => new(false, code, message);
}

public sealed record SetupAdministratorCredential(int Id, string KeyPrefix, string ApiKey);
