using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.Kavenegar;

internal static class KavenegarProviderErrors
{
    public static Error UnknownSenderLine(string senderLine) =>
        Error.Provider(
            KavenegarErrorCodes.UnknownSenderLine,
            UserMessages.Providers.NoKavenegarAccountForSenderLine(senderLine));

    public static Error HttpStatus(int statusCode) =>
        Error.Provider(KavenegarErrorCodes.HttpStatus, UserMessages.Providers.KavenegarHttpStatus(statusCode));

    public static Error BadJson(string message) =>
        Error.Provider(KavenegarErrorCodes.BadJson, UserMessages.Providers.KavenegarBadJson(message));

    public static Error RequestStatus(string method, KavenegarReturn requestReturn)
    {
        string code = requestReturn.Status == KavenegarStatusCodes.TemporaryUnavailable
            ? KavenegarErrorCodes.TemporaryUnavailable
            : KavenegarErrorCodes.RequestStatus;

        return Error.Provider(
            code,
            UserMessages.Providers.KavenegarRequestStatus(method, requestReturn.Status, requestReturn.Message));
    }
}
