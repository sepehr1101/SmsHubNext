namespace SmsHubNext.Features.Providers.Kavenegar;

internal static class KavenegarErrorCodes
{
    public const string UnknownSenderLine = "kavenegar.unknown_sender_line";
    public const string Transport = "kavenegar.transport";
    public const string Timeout = "kavenegar.timeout";
    public const string HttpStatus = "kavenegar.http_status";
    public const string BadJson = "kavenegar.bad_json";
    public const string EmptyBody = "kavenegar.empty_body";
    public const string MissingResult = "kavenegar.missing_result";
    public const string MissingId = "kavenegar.missing_id";
    public const string MessageStatus = "kavenegar.message_status";
    public const string TemporaryUnavailable = "kavenegar.temporary_unavailable";
    public const string RequestStatus = "kavenegar.request_status";
}
