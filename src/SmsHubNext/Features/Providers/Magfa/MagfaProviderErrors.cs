using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.Magfa;

internal static class MagfaProviderErrors
{
    public static Error UnknownSenderLine(string senderLine) =>
        Error.Provider(
            MagfaErrorCodes.UnknownSenderLine,
            UserMessages.Providers.NoMagfaAccountForSenderLine(senderLine));

    public static Error RequestStatus(string code, int status) =>
        Error.Provider(code, UserMessages.Providers.MagfaRequestStatus(status));

    public static Error Transport(string message) =>
        Error.Provider(MagfaErrorCodes.Transport, UserMessages.Providers.MagfaTransport);

    public static Error Timeout(string message) =>
        Error.Provider(MagfaErrorCodes.Timeout, UserMessages.Providers.MagfaTimeout);

    public static Error HttpStatus(int statusCode) =>
        Error.Provider(MagfaErrorCodes.HttpStatus, UserMessages.Providers.MagfaHttpStatus(statusCode));

    public static Error AuthenticationHttpStatus(int statusCode) =>
        Error.Provider(
            $"{MagfaErrorCodes.AuthenticationHttpStatus}.{statusCode}",
            UserMessages.Providers.MagfaHttpStatus(statusCode));

    public static bool TryGetAuthenticationHttpStatus(Error error, out int statusCode)
    {
        string prefix = MagfaErrorCodes.AuthenticationHttpStatus + ".";
        statusCode = 0;
        return error.Code.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(error.Code[prefix.Length..], out statusCode);
    }

    public static Error BadJson(string message) =>
        Error.Provider(MagfaErrorCodes.BadJson, UserMessages.Providers.MagfaBadJson(message));
}
