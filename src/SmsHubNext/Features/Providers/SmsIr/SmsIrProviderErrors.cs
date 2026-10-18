using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.SmsIr;

internal static class SmsIrProviderErrors
{
    public static Error UnknownSenderLine(string senderLine) =>
        Error.Provider(
            SmsIrErrorCodes.UnknownSenderLine,
            UserMessages.Providers.NoSmsIrAccountForSenderLine(senderLine));

    public static Error InvalidSenderLine(string senderLine) =>
        Error.Provider(
            SmsIrErrorCodes.InvalidSenderLine,
            $"sms.ir sender line '{senderLine}' is not numeric.");

    public static Error HttpStatus(int statusCode) =>
        Error.Provider(SmsIrErrorCodes.HttpStatus, UserMessages.Providers.SmsIrHttpStatus(statusCode));

    public static Error AuthenticationHttpStatus(int statusCode) =>
        EncodedHttpError(SmsIrErrorCodes.AuthenticationHttpStatus, statusCode);

    public static Error RateLimitHttpStatus(int statusCode) =>
        EncodedHttpError(SmsIrErrorCodes.RateLimitHttpStatus, statusCode);

    public static Error BadRequestHttpStatus(int statusCode) =>
        EncodedHttpError(SmsIrErrorCodes.BadRequestHttpStatus, statusCode);

    public static bool TryGetHttpStatus(Error error, string errorCode, out int statusCode)
    {
        string prefix = errorCode + ".";
        statusCode = 0;
        return error.Code.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(error.Code[prefix.Length..], out statusCode);
    }

    public static Error BadJson(string message) =>
        Error.Provider(SmsIrErrorCodes.BadJson, UserMessages.Providers.SmsIrBadJson(message));

    public static Error RequestStatus<T>(string method, SmsIrResponse<T> response) =>
        Error.Provider(
            SmsIrErrorCodes.RequestStatus,
            UserMessages.Providers.SmsIrRequestStatus(method, response.Status, response.Message));

    private static Error EncodedHttpError(string code, int statusCode) =>
        Error.Provider($"{code}.{statusCode}", UserMessages.Providers.SmsIrHttpStatus(statusCode));
}
