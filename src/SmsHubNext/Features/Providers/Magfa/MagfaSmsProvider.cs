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
/// Credentials are per <see cref="MagfaAccount"/>, not global: the account is selected per request
/// (the <c>Authorization</c> header is set on each <see cref="HttpRequestMessage"/> rather than on
/// the shared typed client). Send groups its messages by the account that owns each sender line and
/// issues one request per account; the account-scoped reads (statuses/mid/inbox) fan out over every
/// configured account and merge the results.
///
/// Lane discipline (ARCHITECTURE.md §6): a failed <b>outer</b> <see cref="Result"/> is a
/// transport/transient condition for the whole request (the dispatcher re-queues the chunk); a
/// successful outer result carries one <b>inner</b> result per message — a failed inner result is
/// that message's transient failure, a successful inner result its domain outcome. This method
/// never throws for an expected failure.
///
/// The typed <see cref="HttpClient"/> is configured at registration with the base address and
/// timeout; a timeout surfaces here as a <see cref="TaskCanceledException"/> and is reported as
/// transient.
/// </summary>
public sealed class MagfaSmsProvider : ISmsProvider
{
    private const string SendPath = "/api/http/sms/v2/send";
    private const string StatusesPath = "/api/http/sms/v2/statuses/";
    private const string MidPath = "/api/http/sms/v2/mid/";
    private const string MessagesPath = "/api/http/sms/v2/messages/";

    private readonly HttpClient _httpClient;
    private readonly MagfaAccountResolver _accounts;
    private readonly MagfaOptions _options;
    private readonly ILogger<MagfaSmsProvider> _logger;

    public MagfaSmsProvider(
        HttpClient httpClient,
        MagfaAccountResolver accounts,
        MagfaOptions options,
        ILogger<MagfaSmsProvider> logger)
    {
        _httpClient = httpClient;
        _accounts = accounts;
        _options = options;
        _logger = logger;
    }

    public string Name => ProviderCodes.Magfa;

    public int MaxBatchSize => _options.BatchSize;

    public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>([]);

        // One result slot per input request, filled in input order regardless of how the requests are
        // grouped by account below. A request whose sender line maps to no account is a misconfiguration:
        // mark it transient (left queued, retried) and log, so it surfaces rather than being charged.
        Result<ProviderDispatchResult>[] results = new Result<ProviderDispatchResult>[requests.Count];
        Dictionary<MagfaAccount, List<int>> byAccount = new Dictionary<MagfaAccount, List<int>>();

        for (int i = 0; i < requests.Count; i++)
        {
            MagfaAccount? account = _accounts.Resolve(requests[i].SenderLine);
            if (account is null)
            {
                _logger.LogError("No Magfa account is configured for sender line {SenderLine} (message {MessageId}).",
                    requests[i].SenderLine, requests[i].MessageId);
                results[i] = MagfaProviderErrors.UnknownSenderLine(requests[i].SenderLine);
                continue;
            }

            if (!byAccount.TryGetValue(account, out List<int>? indices))
                byAccount[account] = indices = new List<int>();
            indices.Add(i);
        }

        // One /send per account group. A whole-group transport or request-level fault re-queues the
        // entire chunk (the dispatcher reconciles via /mid, so a lost response never double-charges) —
        // the common case is a single account (a batch sends on one line), so this matches one request.
        foreach ((MagfaAccount account, List<int> indices) in byAccount)
        {
            List<ProviderSendRequest> group = indices.Select(i => requests[i]).ToList();

            Result<MagfaSendResponse> response = await ExecuteAsync<MagfaSendResponse>(
                () => CreateSendRequest(account, group), cancellationToken);
            if (response.IsFailure)
                return response.Error!;

            Result<IReadOnlyList<Result<ProviderDispatchResult>>> mapped = MapSendResponse(response.Value, group);
            if (mapped.IsFailure)
                return mapped.Error!;

            for (int j = 0; j < indices.Count; j++)
                results[indices[j]] = mapped.Value[j];
        }

        return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(results);
    }

    public async Task<Result<string?>> ResolveSubmittedMessageIdAsync(
        long messageId, CancellationToken cancellationToken)
    {
        // GET /mid/{uid} — the uid we sent is the message id (reference §6). The uid is account-scoped
        // and we have no sender line here, so ask each account until one has a record for it; an account
        // that never sent it answers mid -1 (treated as "no record"), so a clean miss is "safe to re-send".
        foreach (MagfaAccount account in _accounts.Accounts)
        {
            Result<MagfaMidResponse> response = await ExecuteAsync<MagfaMidResponse>(
                () => CreateGetRequest(account, MidPath + messageId), cancellationToken);
            if (response.IsFailure)
                return response.Error!;

            MagfaMidResponse body = response.Value;
            if (body.Status != MagfaStatusCodes.Success)
            {
                _logger.LogError("Magfa /mid returned request-level status {Status}.", body.Status);
                return MagfaProviderErrors.RequestStatus(MagfaErrorCodes.MidRequestStatus, body.Status);
            }

            // mid >= 0 means this account has a record (already accepted); -1 means it doesn't.
            if (body.Mid >= 0)
                return Result.Success<string?>(body.Mid.ToString());
        }

        return Result.Success<string?>(null);
    }

    public async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        if (providerMessageIds.Count == 0)
            return Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]);

        // GET /statuses/{mid1,mid2,...} — statuses are account-scoped, so ask each account and merge.
        // An account returns DLRs only for the mids it owns; mids belonging to other accounts are simply
        // absent and resolve when that account is queried, so the union covers every id.
        string idsPath = StatusesPath + string.Join(',', providerMessageIds);
        List<ProviderDeliveryReport> reports = new List<ProviderDeliveryReport>(providerMessageIds.Count);

        foreach (MagfaAccount account in _accounts.Accounts)
        {
            Result<MagfaStatusesResponse> response = await ExecuteAsync<MagfaStatusesResponse>(
                () => CreateGetRequest(account, idsPath), cancellationToken);
            if (response.IsFailure)
                return response.Error!;

            MagfaStatusesResponse body = response.Value;
            if (body.Status != MagfaStatusCodes.Success)
            {
                _logger.LogError("Magfa /statuses returned request-level status {Status}.", body.Status);
                return MagfaProviderErrors.RequestStatus(MagfaErrorCodes.StatusesRequestStatus, body.Status);
            }

            reports.AddRange(body.Dlrs.Select(dlr => new ProviderDeliveryReport(
                dlr.Mid.ToString(), MagfaDeliveryStatusCodes.Classify(dlr.Status), dlr.Status)));
        }

        return Result.Success<IReadOnlyList<ProviderDeliveryReport>>(reports);
    }

    public async Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
        int maxCount, CancellationToken cancellationToken)
    {
        // GET /messages/{count} — the inbox is per account and the pull is destructive, so drain each
        // account (up to maxCount each) and merge. Magfa caps count at MaxMessagesPerRequest.
        string path = MessagesPath + maxCount;
        List<ProviderInboundMessage> inbound = new List<ProviderInboundMessage>();

        foreach (MagfaAccount account in _accounts.Accounts)
        {
            Result<MagfaMessagesResponse> response = await ExecuteAsync<MagfaMessagesResponse>(
                () => CreateGetRequest(account, path), cancellationToken);
            if (response.IsFailure)
                return response.Error!;

            MagfaMessagesResponse body = response.Value;
            if (body.Status != MagfaStatusCodes.Success)
            {
                _logger.LogError("Magfa /messages returned request-level status {Status}.", body.Status);
                return MagfaProviderErrors.RequestStatus(MagfaErrorCodes.MessagesRequestStatus, body.Status);
            }

            inbound.AddRange(body.Messages.Select(m =>
                new ProviderInboundMessage(m.SenderNumber, m.RecipientNumber, m.Body, m.Date)));
        }

        return Result.Success<IReadOnlyList<ProviderInboundMessage>>(inbound);
    }

    // Builds the POST /send for one account's group of messages. Parallel arrays, one element per
    // message: uids carries each message id so the response can be correlated back per message
    // (reference §5/§6); senders repeats per recipient to satisfy Magfa's equal-length rule.
    private static HttpRequestMessage CreateSendRequest(MagfaAccount account, IReadOnlyList<ProviderSendRequest> group)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, SendPath)
        {
            Content = JsonContent.Create(new
            {
                senders = group.Select(r => r.SenderLine).ToArray(),
                recipients = group.Select(r => r.MobileNumber).ToArray(),
                messages = group.Select(r => r.Body).ToArray(),
                uids = group.Select(r => r.MessageId).ToArray(),
            }),
        };
        request.Headers.Authorization = account.AuthorizationHeader;
        return request;
    }

    private static HttpRequestMessage CreateGetRequest(MagfaAccount account, string path)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = account.AuthorizationHeader;
        return request;
    }

    /// <summary>
    /// Issues one Magfa request and deserializes its JSON body. All transport-boundary failures —
    /// connection errors, timeouts, non-success status codes, and malformed/empty bodies — are turned
    /// into a transient provider <see cref="Error"/> here, so callers deal only with a parsed body.
    /// </summary>
    private async Task<Result<T>> ExecuteAsync<T>(
        Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
        where T : class
    {
        using HttpRequestMessage request = createRequest();

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return MagfaProviderErrors.Transport(ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return MagfaProviderErrors.Timeout(ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return MagfaProviderErrors.HttpStatus((int)response.StatusCode);

            T? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (JsonException ex)
            {
                return MagfaProviderErrors.BadJson(ex.Message);
            }

            return body is null
                ? Error.Provider(MagfaErrorCodes.EmptyBody, UserMessages.Providers.MagfaEmptyBody)
                : Result.Success(body);
        }
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
        return MagfaProviderErrors.RequestStatus(MagfaErrorCodes.RequestStatus, response.Status);
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
                results.Add(Error.Provider(MagfaErrorCodes.MissingResult, UserMessages.Providers.MagfaMissingResult));
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
                    return Error.Provider(MagfaErrorCodes.MissingId, UserMessages.Providers.MagfaMissingId);
                return ProviderDispatchResult.Accepted(message.Id.Value.ToString(), message.Status);

            case MagfaDisposition.InsufficientCredit:
                return ProviderDispatchResult.InsufficientCredit(message.Status);

            case MagfaDisposition.Rejected:
                return ProviderDispatchResult.Rejected(message.Status, UserMessages.Providers.MagfaRejectedStatus(message.Status));

            case MagfaDisposition.Transient:
                return Error.Provider(MagfaErrorCodes.MessageStatus, UserMessages.Providers.MagfaMessageStatus(message.Status));

            // A per-message configuration fault (e.g. invalid sender/encoding) will never succeed on
            // retry, so it is treated as a rejection (unsendable ⇒ refund), logged as a likely config issue.
            default:
                _logger.LogWarning(
                    "Magfa refused message {MessageId} with status {Status} (likely a configuration issue); rejecting.",
                    request.MessageId, message.Status);
                return ProviderDispatchResult.Rejected(
                    message.Status,
                    UserMessages.Providers.MagfaRejectedConfigurationStatus(message.Status));
        }
    }
}
