namespace SmsHubNext.Features.Providers.SmsIr;

internal static class SmsIrErrorCodes
{
    public const string UnknownSenderLine = "smsir.unknown_sender_line";
    public const string InvalidSenderLine = "smsir.invalid_sender_line";
    public const string Transport = "smsir.transport";
    public const string Timeout = "smsir.timeout";
    public const string HttpStatus = "smsir.http_status";
    public const string AuthenticationHttpStatus = "smsir.authentication_http_status";
    public const string RateLimitHttpStatus = "smsir.rate_limit_http_status";
    public const string BadRequestHttpStatus = "smsir.bad_request_http_status";
    public const string BadJson = "smsir.bad_json";
    public const string EmptyBody = "smsir.empty_body";
    public const string MissingResult = "smsir.missing_result";
    public const string RequestStatus = "smsir.request_status";
}
