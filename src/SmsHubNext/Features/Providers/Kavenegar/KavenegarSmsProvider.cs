using System.Net.Http.Json;
using System.Text.Json;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.Kavenegar;

/// <summary>
/// Kavenegar REST implementation. Submission intentionally uses only <c>/sms/sendarray.json</c>;
/// even a one-message dispatch goes through the batch endpoint so the provider seam stays small.
/// </summary>
public sealed class KavenegarSmsProvider : ISmsProvider
{
    private readonly HttpClient _httpClient;
    private readonly KavenegarAccountResolver _accounts;
    private readonly KavenegarOptions _options;
    private readonly ILogger<KavenegarSmsProvider> _logger;

    public KavenegarSmsProvider(
        HttpClient httpClient,
        KavenegarAccountResolver accounts,
        KavenegarOptions options,
        ILogger<KavenegarSmsProvider> logger)
    {
        _httpClient = httpClient;
        _accounts = accounts;
        _options = options;
        _logger = logger;
    }

    public string Name => ProviderCodes.Kavenegar;

    public int MaxBatchSize => _options.BatchSize;

    public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>([]);

        Result<ProviderDispatchResult>[] results = new Result<ProviderDispatchResult>[requests.Count];
        Dictionary<KavenegarAccount, List<int>> byAccount = new Dictionary<KavenegarAccount, List<int>>();

        for (int i = 0; i < requests.Count; i++)
        {
            KavenegarAccount? account = _accounts.Resolve(requests[i].SenderLine);
            if (account is null)
            {
                _logger.LogError("No Kavenegar account is configured for sender line {SenderLine} (message {MessageId}).",
                    requests[i].SenderLine, requests[i].MessageId);
                results[i] = KavenegarProviderErrors.UnknownSenderLine(requests[i].SenderLine);
                continue;
            }

            if (!byAccount.TryGetValue(account, out List<int>? indices))
                byAccount[account] = indices = new List<int>();
            indices.Add(i);
        }

        foreach ((KavenegarAccount account, List<int> indices) in byAccount)
        {
            List<ProviderSendRequest> group = indices.Select(i => requests[i]).ToList();
            Result<KavenegarResponse<IReadOnlyList<KavenegarMessageEntry>>> response =
                await ExecuteAsync<IReadOnlyList<KavenegarMessageEntry>>(
                    () => CreateSendArrayRequest(account, group), cancellationToken);

            if (response.IsFailure)
                return response.Error!;

            Result<IReadOnlyList<Result<ProviderDispatchResult>>> mapped =
                MapSendArrayResponse(response.Value, group);
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
        // Recovery starts from our local id. Kavenegar Select needs provider messageid, so the
        // duplicate-send guard is statuslocalmessageid; Select is only useful after this recovers an id.
        foreach (KavenegarAccount account in _accounts.Accounts)
        {
            Result<KavenegarResponse<IReadOnlyList<KavenegarMessageEntry>>> response =
                await ExecuteAsync<IReadOnlyList<KavenegarMessageEntry>>(
                    () => CreateGetRequest(account, $"sms/statuslocalmessageid.json?localid={messageId}"),
                    cancellationToken);

            if (response.IsFailure)
                return response.Error!;

            if (response.Value.Return.Status != KavenegarStatusCodes.Success)
                return RequestError("statuslocalmessageid", response.Value.Return);

            KavenegarMessageEntry? entry = response.Value.Entries?.FirstOrDefault();
            if (entry is null || entry.Status == KavenegarStatusCodes.InvalidMessageId)
                continue;

            if (entry.MessageId is long providerMessageId)
                return Result.Success<string?>(providerMessageId.ToString());
        }

        return Result.Success<string?>(null);
    }

    public async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        if (providerMessageIds.Count == 0)
            return Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]);

        List<ProviderDeliveryReport> reports = new List<ProviderDeliveryReport>(providerMessageIds.Count);

        foreach (KavenegarAccount account in _accounts.Accounts)
        {
            foreach (string[] chunk in providerMessageIds.Chunk(KavenegarOptions.MaxStatusesPerRequest))
            {
                string ids = string.Join(',', chunk);
                Result<KavenegarResponse<IReadOnlyList<KavenegarMessageEntry>>> response =
                    await ExecuteAsync<IReadOnlyList<KavenegarMessageEntry>>(
                        () => CreateGetRequest(account, $"sms/status.json?messageid={ids}"),
                        cancellationToken);

                if (response.IsFailure)
                    return response.Error!;

                if (response.Value.Return.Status != KavenegarStatusCodes.Success)
                    return RequestError("status", response.Value.Return);

                if (response.Value.Entries is null)
                    continue;

                reports.AddRange(response.Value.Entries
                    .Where(entry => entry.MessageId is not null)
                    .Select(entry => new ProviderDeliveryReport(
                        entry.MessageId!.Value.ToString(),
                        KavenegarStatusCodes.ClassifyDelivery(entry.Status),
                        entry.Status)));
            }
        }

        return Result.Success<IReadOnlyList<ProviderDeliveryReport>>(reports);
    }

    public async Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
        int maxCount, CancellationToken cancellationToken)
    {
        List<ProviderInboundMessage> inbound = new List<ProviderInboundMessage>();

        foreach (KavenegarAccount account in _accounts.Accounts)
        {
            foreach (string line in account.InboundLines.Take(Math.Max(maxCount, 0)))
            {
                Result<KavenegarResponse<IReadOnlyList<KavenegarInboundEntry>>> response =
                    await ExecuteAsync<IReadOnlyList<KavenegarInboundEntry>>(
                        () => CreateGetRequest(account, $"sms/receive.json?linenumber={Uri.EscapeDataString(line)}&isread=0"),
                        cancellationToken);

                if (response.IsFailure)
                    return response.Error!;

                if (response.Value.Return.Status != KavenegarStatusCodes.Success)
                    return RequestError("receive", response.Value.Return);

                if (response.Value.Entries is null)
                    continue;

                inbound.AddRange(response.Value.Entries.Select(entry => new ProviderInboundMessage(
                    entry.Sender,
                    entry.Receptor,
                    entry.Message,
                    entry.Date.ToString())));
            }
        }

        return Result.Success<IReadOnlyList<ProviderInboundMessage>>(inbound);
    }

    private static HttpRequestMessage CreateSendArrayRequest(
        KavenegarAccount account, IReadOnlyList<ProviderSendRequest> group)
    {
        Dictionary<string, string> form = new Dictionary<string, string>
        {
            ["receptor"] = JsonSerializer.Serialize(group.Select(r => r.MobileNumber).ToArray()),
            ["sender"] = JsonSerializer.Serialize(group.Select(r => r.SenderLine).ToArray()),
            ["message"] = JsonSerializer.Serialize(group.Select(r => r.Body).ToArray()),
            ["localmessageids"] = JsonSerializer.Serialize(group.Select(r => r.MessageId).ToArray()),
        };

        return new HttpRequestMessage(HttpMethod.Post, Path(account, "sms/sendarray.json"))
        {
            Content = new FormUrlEncodedContent(form),
        };
    }

    private static HttpRequestMessage CreateGetRequest(KavenegarAccount account, string path) =>
        new(HttpMethod.Get, Path(account, path));

    private static string Path(KavenegarAccount account, string path) =>
        $"v1/{Uri.EscapeDataString(account.ApiKey)}/{path}";

    private async Task<Result<KavenegarResponse<T>>> ExecuteAsync<T>(
        Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = createRequest();

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Error.Provider(KavenegarErrorCodes.Transport, UserMessages.Providers.KavenegarTransport);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Provider(KavenegarErrorCodes.Timeout, UserMessages.Providers.KavenegarTimeout);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return KavenegarProviderErrors.HttpStatus((int)response.StatusCode);

            KavenegarResponse<T>? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<KavenegarResponse<T>>(cancellationToken);
            }
            catch (JsonException ex)
            {
                return KavenegarProviderErrors.BadJson(ex.Message);
            }

            if (body is null)
                return Error.Provider(KavenegarErrorCodes.EmptyBody, UserMessages.Providers.KavenegarEmptyBody);

            return Result.Success(body);
        }
    }

    private Result<IReadOnlyList<Result<ProviderDispatchResult>>> MapSendArrayResponse(
        KavenegarResponse<IReadOnlyList<KavenegarMessageEntry>> response,
        IReadOnlyList<ProviderSendRequest> requests)
    {
        if (response.Return.Status == KavenegarStatusCodes.InsufficientCredit)
        {
            IReadOnlyList<Result<ProviderDispatchResult>> outOfCredit = requests
                .Select(_ => Result.Success(ProviderDispatchResult.InsufficientCredit(response.Return.Status)))
                .ToList();
            return Result.Success(outOfCredit);
        }

        if (response.Return.Status != KavenegarStatusCodes.Success)
            return RequestError("sendarray", response.Return);

        IReadOnlyList<KavenegarMessageEntry> entries = response.Entries ?? [];
        Dictionary<long, KavenegarMessageEntry> byLocalId = new Dictionary<long, KavenegarMessageEntry>(entries.Count);
        foreach (KavenegarMessageEntry entry in entries)
        {
            if (long.TryParse(entry.LocalId, out long localId))
                byLocalId[localId] = entry;
        }

        bool positionalFallback = entries.Count == requests.Count;
        List<Result<ProviderDispatchResult>> results = new List<Result<ProviderDispatchResult>>(requests.Count);

        for (int i = 0; i < requests.Count; i++)
        {
            ProviderSendRequest request = requests[i];
            KavenegarMessageEntry? entry = byLocalId.TryGetValue(request.MessageId, out KavenegarMessageEntry? matched)
                ? matched
                : positionalFallback ? entries[i] : null;

            if (entry is null)
            {
                _logger.LogWarning("Kavenegar returned no result for message {MessageId}; will retry.", request.MessageId);
                results.Add(Error.Provider(KavenegarErrorCodes.MissingResult, UserMessages.Providers.KavenegarMissingResult));
                continue;
            }

            results.Add(MapMessage(entry, request));
        }

        return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(results);
    }

    private static Result<ProviderDispatchResult> MapMessage(
        KavenegarMessageEntry entry, ProviderSendRequest request)
    {
        if (KavenegarStatusCodes.IsAcceptedSendStatus(entry.Status))
        {
            if (entry.MessageId is null)
                return Error.Provider(KavenegarErrorCodes.MissingId, UserMessages.Providers.KavenegarMissingId);
            return ProviderDispatchResult.Accepted(entry.MessageId.Value.ToString(), entry.Status);
        }

        if (entry.Status == KavenegarStatusCodes.InsufficientCredit)
            return ProviderDispatchResult.InsufficientCredit(entry.Status);

        if (KavenegarStatusCodes.IsRejectedSendStatus(entry.Status))
            return ProviderDispatchResult.Rejected(entry.Status, UserMessages.Providers.KavenegarRejectedStatus(entry.Status));

        return Error.Provider(
            KavenegarErrorCodes.MessageStatus,
            UserMessages.Providers.KavenegarMessageStatus(request.MessageId, entry.Status));
    }

    private static Error RequestError(string method, KavenegarReturn requestReturn)
    {
        return KavenegarProviderErrors.RequestStatus(method, requestReturn);
    }
}
