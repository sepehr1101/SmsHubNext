using System.Net.Http.Json;
using System.Text.Json;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// The Magfa HTTP v2 implementation of <see cref="ISmsProvider"/> (API reference under
/// <c>docs/providers/magfa-http-v2.md</c>). Submits one message per call to <c>POST /send</c>
/// and maps the single per-recipient result onto a <see cref="ProviderDispatchResult"/>.
///
/// Lane discipline (ARCHITECTURE.md §6): a <b>failed <see cref="Result"/></b> means a
/// transport/transient condition and the dispatcher re-queues the batch; a <b>successful
/// Result</b> carries the provider's domain outcome (accepted / rejected / out-of-credit).
/// This method never throws for an expected failure.
///
/// The typed <see cref="HttpClient"/> is configured at registration with the base address,
/// Basic-auth header, and timeout (see <c>ServiceCollectionExtensions</c>); a timeout surfaces
/// here as a <see cref="TaskCanceledException"/> and is reported as transient.
/// </summary>
public sealed class MagfaSmsProvider : ISmsProvider
{
    private const string SendPath = "/api/http/sms/v2/send";
    private const string StatusesPath = "/api/http/sms/v2/statuses/";

    private readonly HttpClient _httpClient;
    private readonly ILogger<MagfaSmsProvider> _logger;

    public MagfaSmsProvider(HttpClient httpClient, ILogger<MagfaSmsProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Name => "magfa";

    public async Task<Result<ProviderDispatchResult>> SendAsync(
        ProviderSendRequest request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            // Single-message dispatch: parallel single-element arrays. uids carries our message id
            // so a later /mid lookup can recover from a send that timed out (reference §6). The
            // payload is passed inline so System.Text.Json serializes the anonymous type directly.
            response = await _httpClient.PostAsJsonAsync(
                SendPath,
                new
                {
                    senders = new[] { request.SenderLine },
                    recipients = new[] { request.MobileNumber },
                    messages = new[] { request.Body },
                    uids = new[] { request.MessageId },
                },
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return Transient("magfa.transport", $"HTTP transport error: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Transient("magfa.timeout", $"Request timed out: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
            return Transient("magfa.http_status", $"Magfa returned HTTP {(int)response.StatusCode}.");

        MagfaSendResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<MagfaSendResponse>(cancellationToken);
        }
        catch (JsonException ex)
        {
            return Transient("magfa.bad_json", $"Could not parse Magfa response: {ex.Message}");
        }

        if (body is null)
            return Transient("magfa.empty_body", "Magfa returned an empty response body.");

        return Map(body, request);
    }

    public async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        if (providerMessageIds.Count == 0)
            return Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]);

        // GET /statuses/{mid1,mid2,...} — up to 100 ids (the poller batches to that limit).
        string path = StatusesPath + string.Join(',', providerMessageIds);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(path, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return Error.Provider("magfa.transport", $"HTTP transport error: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Provider("magfa.timeout", $"Request timed out: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
            return Error.Provider("magfa.http_status", $"Magfa returned HTTP {(int)response.StatusCode}.");

        MagfaStatusesResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<MagfaStatusesResponse>(cancellationToken);
        }
        catch (JsonException ex)
        {
            return Error.Provider("magfa.bad_json", $"Could not parse Magfa statuses response: {ex.Message}");
        }

        if (body is null)
            return Error.Provider("magfa.empty_body", "Magfa returned an empty statuses body.");

        if (body.Status != MagfaStatusCodes.Success)
        {
            _logger.LogError("Magfa /statuses returned request-level status {Status}.", body.Status);
            return Error.Provider("magfa.statuses_request_status", $"Magfa request-level status {body.Status}.");
        }

        List<ProviderDeliveryReport> reports = new(body.Dlrs.Count);
        foreach (MagfaDlr dlr in body.Dlrs)
        {
            reports.Add(new ProviderDeliveryReport(
                dlr.Mid.ToString(),
                MagfaDeliveryStatusCodes.Classify(dlr.Status),
                dlr.Status));
        }

        return Result.Success<IReadOnlyList<ProviderDeliveryReport>>(reports);
    }

    private Result<ProviderDispatchResult> Map(MagfaSendResponse body, ProviderSendRequest request)
    {
        // A non-zero request-level status applies to the whole call, before any per-message result.
        if (body.Status != MagfaStatusCodes.Success)
        {
            MagfaDisposition top = MagfaStatusCodes.Classify(body.Status);
            if (top == MagfaDisposition.InsufficientCredit)
                return ProviderDispatchResult.InsufficientCredit(body.Status);

            // Auth/IP/protocol faults and "server busy" are not per-message outcomes: re-queue and
            // surface loudly so a misconfiguration is fixed rather than charged/refunded per message.
            _logger.LogError(
                "Magfa rejected the request for message {MessageId} with request-level status {Status}.",
                request.MessageId, body.Status);
            return Transient("magfa.request_status", $"Magfa request-level status {body.Status}.");
        }

        if (body.Messages.Count == 0)
            return Transient("magfa.no_message_result", "Magfa accepted the request but returned no message result.");

        MagfaSentMessage message = body.Messages[0];
        MagfaDisposition disposition = MagfaStatusCodes.Classify(message.Status);

        switch (disposition)
        {
            case MagfaDisposition.Accepted:
                if (message.Id is null)
                    return Transient("magfa.missing_id", "Magfa accepted the message but returned no id.");
                return ProviderDispatchResult.Accepted(message.Id.Value.ToString(), message.Status);

            case MagfaDisposition.InsufficientCredit:
                return ProviderDispatchResult.InsufficientCredit(message.Status);

            case MagfaDisposition.Rejected:
                return ProviderDispatchResult.Rejected(message.Status, $"Magfa status {message.Status}.");

            case MagfaDisposition.Transient:
                return Transient("magfa.message_status", $"Magfa message status {message.Status}.");

            // A per-message configuration fault (e.g. invalid sender/encoding) will never succeed on
            // retry, so it is treated as a rejection (the message is unsendable ⇒ refund), logged as a
            // likely configuration issue rather than a recipient problem.
            default:
                _logger.LogWarning(
                    "Magfa refused message {MessageId} with status {Status} (likely a configuration issue); rejecting.",
                    request.MessageId, message.Status);
                return ProviderDispatchResult.Rejected(message.Status, $"Magfa status {message.Status} (configuration).");
        }
    }

    private static Result<ProviderDispatchResult> Transient(string code, string detail) =>
        Error.Provider(code, detail);
}
