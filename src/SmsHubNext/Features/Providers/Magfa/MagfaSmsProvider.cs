using System.Net.Http.Json;
using System.Text.Json;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// The Magfa HTTP v2 implementation of <see cref="ISmsProvider"/> (API reference under
/// <c>docs/providers/magfa-http-v2.md</c>). Submits up to <see cref="MaxBatchSize"/> messages per
/// <c>POST /send</c> as parallel arrays and maps each per-recipient result back to its request,
/// and queries <c>GET /statuses</c> for delivery reports.
///
/// Lane discipline (ARCHITECTURE.md §6): a failed <b>outer</b> <see cref="Result"/> is a
/// transport/transient condition for the whole request (the dispatcher re-queues the chunk); a
/// successful outer result carries one <b>inner</b> result per message — a failed inner result is
/// that message's transient failure, a successful inner result its domain outcome. This method
/// never throws for an expected failure.
///
/// The typed <see cref="HttpClient"/> is configured at registration with the base address,
/// Basic-auth header, and timeout; a timeout surfaces here as a <see cref="TaskCanceledException"/>
/// and is reported as transient.
/// </summary>
public sealed class MagfaSmsProvider : ISmsProvider
{
    private const string SendPath = "/api/http/sms/v2/send";
    private const string StatusesPath = "/api/http/sms/v2/statuses/";
    private const string MidPath = "/api/http/sms/v2/mid/";

    private readonly HttpClient _httpClient;
    private readonly MagfaOptions _options;
    private readonly ILogger<MagfaSmsProvider> _logger;

    public MagfaSmsProvider(HttpClient httpClient, MagfaOptions options, ILogger<MagfaSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public string Name => "magfa";

    public int MaxBatchSize => _options.BatchSize;

    public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>([]);

        // Parallel arrays, one element per message. uids carries each message id so the response can
        // be correlated back per message (reference §5/§6); senders repeats per recipient to satisfy
        // Magfa's equal-length rule even if a future batch mixes lines.
        Result<MagfaSendResponse> response = await ExecuteAsync<MagfaSendResponse>(
            token => _httpClient.PostAsJsonAsync(
                SendPath,
                new
                {
                    senders = requests.Select(r => r.SenderLine).ToArray(),
                    recipients = requests.Select(r => r.MobileNumber).ToArray(),
                    messages = requests.Select(r => r.Body).ToArray(),
                    uids = requests.Select(r => r.MessageId).ToArray(),
                },
                token),
            cancellationToken);

        return response.IsFailure
            ? response.Error!
            : MapSendResponse(response.Value, requests);
    }

    public async Task<Result<string?>> ResolveSubmittedMessageIdAsync(
        long messageId, CancellationToken cancellationToken)
    {
        // GET /mid/{uid} — the uid we sent is the message id (reference §6).
        Result<MagfaMidResponse> response = await ExecuteAsync<MagfaMidResponse>(
            token => _httpClient.GetAsync(MidPath + messageId, token), cancellationToken);

        if (response.IsFailure)
            return response.Error!;

        MagfaMidResponse body = response.Value;
        if (body.Status != MagfaStatusCodes.Success)
        {
            _logger.LogError("Magfa /mid returned request-level status {Status}.", body.Status);
            return Error.Provider("magfa.mid_request_status", $"Magfa request-level status {body.Status}.");
        }

        // mid -1 means Magfa has no record for this uid (safe to re-send).
        return body.Mid < 0
            ? Result.Success<string?>(null)
            : Result.Success<string?>(body.Mid.ToString());
    }

    public async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        if (providerMessageIds.Count == 0)
            return Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]);

        // GET /statuses/{mid1,mid2,...} — up to MaxBatchSize ids (the poller batches to that limit).
        Result<MagfaStatusesResponse> response = await ExecuteAsync<MagfaStatusesResponse>(
            token => _httpClient.GetAsync(StatusesPath + string.Join(',', providerMessageIds), token),
            cancellationToken);

        if (response.IsFailure)
            return response.Error!;

        MagfaStatusesResponse body = response.Value;
        if (body.Status != MagfaStatusCodes.Success)
        {
            _logger.LogError("Magfa /statuses returned request-level status {Status}.", body.Status);
            return Error.Provider("magfa.statuses_request_status", $"Magfa request-level status {body.Status}.");
        }

        return Result.Success<IReadOnlyList<ProviderDeliveryReport>>(body.Dlrs
            .Select(dlr => new ProviderDeliveryReport(
                dlr.Mid.ToString(), MagfaDeliveryStatusCodes.Classify(dlr.Status), dlr.Status))
            .ToList());
    }

    /// <summary>
    /// Issues one Magfa request and deserializes its JSON body. All transport-boundary failures —
    /// connection errors, timeouts, non-success status codes, and malformed/empty bodies — are turned
    /// into a transient provider <see cref="Error"/> here, so callers deal only with a parsed body.
    /// </summary>
    private async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
        where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await send(cancellationToken);
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

        T? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException ex)
        {
            return Error.Provider("magfa.bad_json", $"Could not parse Magfa response: {ex.Message}");
        }

        return body is null
            ? Error.Provider("magfa.empty_body", "Magfa returned an empty response body.")
            : Result.Success(body);
    }

    private Result<IReadOnlyList<Result<ProviderDispatchResult>>> MapSendResponse(
        MagfaSendResponse response, IReadOnlyList<ProviderSendRequest> requests)
    {
        // A non-zero request-level status applies to the whole chunk, before any per-message result.
        if (response.Status == MagfaStatusCodes.Success)
            return Result.Success(CorrelateResults(response, requests));

        if (MagfaStatusCodes.Classify(response.Status) == MagfaDisposition.InsufficientCredit)
        {
            // The whole request was blocked on credit: every message is out-of-credit, so the
            // dispatcher holds the batch and leaves them queued.
            IReadOnlyList<Result<ProviderDispatchResult>> outOfCredit = requests
                .Select(_ => Result.Success(ProviderDispatchResult.InsufficientCredit(response.Status)))
                .ToList();
            return Result.Success(outOfCredit);
        }

        // Auth/IP/protocol faults and "server busy" are not per-message outcomes: re-queue the whole
        // chunk and surface loudly so a misconfiguration is fixed rather than charged.
        _logger.LogError("Magfa rejected the send request ({Count} message(s)) with request-level status {Status}.",
            requests.Count, response.Status);
        return Error.Provider("magfa.request_status", $"Magfa request-level status {response.Status}.");
    }

    /// <summary>
    /// Aligns Magfa's per-recipient results to the input requests by the uid we sent (Message.Id), so
    /// a reordered response still maps correctly. When Magfa returned exactly one result per message
    /// without echoing uids, falls back to position; an otherwise-unmatched message is left transient
    /// (retried). Always returns one result per request, in input order.
    /// </summary>
    private IReadOnlyList<Result<ProviderDispatchResult>> CorrelateResults(
        MagfaSendResponse response, IReadOnlyList<ProviderSendRequest> requests)
    {
        Dictionary<long, MagfaSentMessage> byUid = new Dictionary<long, MagfaSentMessage>(response.Messages.Count);
        foreach (MagfaSentMessage sent in response.Messages)
        {
            if (sent.UserId is long uid)
                byUid[uid] = sent;
        }

        bool positionalFallback = response.Messages.Count == requests.Count;

        List<Result<ProviderDispatchResult>> results = new List<Result<ProviderDispatchResult>>(requests.Count);
        for (int i = 0; i < requests.Count; i++)
        {
            ProviderSendRequest request = requests[i];
            MagfaSentMessage? sent = byUid.TryGetValue(request.MessageId, out MagfaSentMessage? matched)
                ? matched
                : positionalFallback ? response.Messages[i] : null;

            if (sent is null)
            {
                _logger.LogWarning("Magfa returned no result for message {MessageId}; will retry.", request.MessageId);
                results.Add(Error.Provider("magfa.missing_result", "Magfa returned no result for this message."));
            }
            else
            {
                results.Add(MapMessage(sent, request));
            }
        }

        return results;
    }

    /// <summary>Maps one per-recipient result to a domain outcome (success) or a per-message transient
    /// failure (failed result). Mirrors the single-message policy in the API reference §8.</summary>
    private Result<ProviderDispatchResult> MapMessage(MagfaSentMessage message, ProviderSendRequest request)
    {
        switch (MagfaStatusCodes.Classify(message.Status))
        {
            case MagfaDisposition.Accepted:
                if (message.Id is null)
                    return Error.Provider("magfa.missing_id", "Magfa accepted the message but returned no id.");
                return ProviderDispatchResult.Accepted(message.Id.Value.ToString(), message.Status);

            case MagfaDisposition.InsufficientCredit:
                return ProviderDispatchResult.InsufficientCredit(message.Status);

            case MagfaDisposition.Rejected:
                return ProviderDispatchResult.Rejected(message.Status, $"Magfa status {message.Status}.");

            case MagfaDisposition.Transient:
                return Error.Provider("magfa.message_status", $"Magfa message status {message.Status}.");

            // A per-message configuration fault (e.g. invalid sender/encoding) will never succeed on
            // retry, so it is treated as a rejection (unsendable ⇒ refund), logged as a likely config issue.
            default:
                _logger.LogWarning(
                    "Magfa refused message {MessageId} with status {Status} (likely a configuration issue); rejecting.",
                    request.MessageId, message.Status);
                return ProviderDispatchResult.Rejected(message.Status, $"Magfa status {message.Status} (configuration).");
        }
    }
}
