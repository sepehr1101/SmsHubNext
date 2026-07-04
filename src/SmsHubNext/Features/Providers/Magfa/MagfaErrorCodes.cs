namespace SmsHubNext.Features.Providers.Magfa;

internal static class MagfaErrorCodes
{
    public const string UnknownSenderLine = "magfa.unknown_sender_line";
    public const string MidRequestStatus = "magfa.mid_request_status";
    public const string StatusesRequestStatus = "magfa.statuses_request_status";
    public const string MessagesRequestStatus = "magfa.messages_request_status";
    public const string Transport = "magfa.transport";
    public const string Timeout = "magfa.timeout";
    public const string HttpStatus = "magfa.http_status";
    public const string BadJson = "magfa.bad_json";
    public const string EmptyBody = "magfa.empty_body";
    public const string RequestStatus = "magfa.request_status";
    public const string MissingResult = "magfa.missing_result";
    public const string MissingId = "magfa.missing_id";
    public const string MessageStatus = "magfa.message_status";
}
