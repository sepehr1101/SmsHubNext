using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.SmsIr;

/// <summary>
/// sms.ir REST implementation. Distinct message bodies are submitted through
/// <c>/v1/send/likeToLike</c>, which preserves input order in its message-id array.
/// </summary>
public sealed class SmsIrSmsProvider : ISmsProvider
{
    private readonly HttpClient _httpClient;
    private readonly SmsIrAccountResolver _accounts;
    private readonly SmsIrOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SmsIrSmsProvider> _logger;
    private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
    private DateTimeOffset _nextRequestAtUtc = DateTimeOffset.MinValue;

    public SmsIrSmsProvider(
        HttpClient httpClient,
        SmsIrAccountResolver accounts,
        SmsIrOptions options,
        TimeProvider clock,
        ILogger<SmsIrSmsProvider> logger)
    {
        _httpClient = httpClient;
        _accounts = accounts;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public string Name => ProviderCodes.SmsIr;

    public int MaxBatchSize => _options.BatchSize;

    // sms.ir does not accept our Message.Id as a correlation key and documents no idempotent
    // duplicate-send guard. An unknown submission must therefore be held for manual review.
    public bool SupportsIdempotentResend => false;

    public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>([]);

        Result<ProviderDispatchResult>[] results = new Result<ProviderDispatchResult>[requests.Count];
        Dictionary<SmsIrSendGroupKey, List<int>> groups = new Dictionary<SmsIrSendGroupKey, List<int>>();

        for (int i = 0; i < requests.Count; i++)
        {
            ProviderSendRequest request = requests[i];
            SmsIrAccount? account = await _accounts.ResolveAsync(request.SenderLine, cancellationToken);
            if (account is null)
            {
                _logger.LogError(
                    "No sms.ir account is configured for sender line {SenderLine} (message {MessageId}).",
                    request.SenderLine,
                    request.MessageId);
                results[i] = SmsIrProviderErrors.UnknownSenderLine(request.SenderLine);
                continue;
            }

            if (!long.TryParse(request.SenderLine, NumberStyles.None, CultureInfo.InvariantCulture, out long lineNumber))
            {
                results[i] = SmsIrProviderErrors.InvalidSenderLine(request.SenderLine);
                continue;
            }

            SmsIrSendGroupKey key = new SmsIrSendGroupKey(account, request.SenderLine, lineNumber);
            if (!groups.TryGetValue(key, out List<int>? indices))
                groups[key] = indices = new List<int>();
            indices.Add(i);
        }

        foreach ((SmsIrSendGroupKey key, List<int> groupIndices) in groups)
        {
            foreach (int[] indices in groupIndices.Chunk(_options.BatchSize))
            {
                List<ProviderSendRequest> group = indices.Select(index => requests[index]).ToList();
                Result<SmsIrResponse<SmsIrSendData>> response = await ExecuteAsync<SmsIrSendData>(
                    () => CreateSendRequest(key.Account, key.LineNumber, group),
                    cancellationToken);

                if (response.IsFailure)
                {
                    Result<IReadOnlyList<Result<ProviderDispatchResult>>> mappedHttpError =
                        MapSendHttpError(response.Error!, group);
                    if (mappedHttpError.IsFailure)
                        return mappedHttpError.Error!;

                    for (int j = 0; j < indices.Length; j++)
                        results[indices[j]] = mappedHttpError.Value[j];
                    continue;
                }

                Result<IReadOnlyList<Result<ProviderDispatchResult>>> mapped =
                    MapSendResponse(response.Value, group);
                if (mapped.IsFailure)
                    return mapped.Error!;

                for (int j = 0; j < indices.Length; j++)
                    results[indices[j]] = mapped.Value[j];
            }
        }

        return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(results);
    }

    public Task<Result<string?>> ResolveSubmittedMessageIdAsync(
        long messageId,
        CancellationToken cancellationToken)
    {
        // There is no sms.ir lookup by our local Message.Id. Returning null is evidence only that
        // the provider offers no correlation lookup; SupportsIdempotentResend prevents auto-resend.
        return Task.FromResult(Result.Success<string?>(null));
    }

    public async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds,
        CancellationToken cancellationToken)
    {
        if (providerMessageIds.Count == 0)
            return Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]);

        IReadOnlyList<SmsIrAccount> accounts = await _accounts.GetAccountsAsync(cancellationToken);
        List<ProviderDeliveryReport> reports = new List<ProviderDeliveryReport>(providerMessageIds.Count);

        foreach (string providerMessageId in providerMessageIds)
        {
            foreach (SmsIrAccount account in accounts)
            {
                Result<SmsIrResponse<SmsIrMessageData>> response = await ExecuteAsync<SmsIrMessageData>(
                    () => CreateGetRequest(account, $"v1/send/{Uri.EscapeDataString(providerMessageId)}"),
                    cancellationToken);

                if (response.IsFailure)
                    return response.Error!;

                if (response.Value.Status == SmsIrStatusCodes.NotFound)
                    continue;

                if (response.Value.Status != SmsIrStatusCodes.Success)
                    return SmsIrProviderErrors.RequestStatus("send/{messageId}", response.Value);

                if (response.Value.Data is null)
                    return Error.Provider(SmsIrErrorCodes.EmptyBody, UserMessages.Providers.SmsIrEmptyBody);

                int rawStatusCode = response.Value.Data.DeliveryState ?? 0;
                reports.Add(new ProviderDeliveryReport(
                    response.Value.Data.MessageId.ToString(CultureInfo.InvariantCulture),
                    SmsIrStatusCodes.ClassifyDelivery(response.Value.Data.DeliveryState),
                    rawStatusCode));
                break;
            }
        }

        return Result.Success<IReadOnlyList<ProviderDeliveryReport>>(reports);
    }

    public async Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        int remaining = Math.Min(Math.Max(maxCount, 0), SmsIrOptions.MaxInboundMessagesPerRequest);
        if (remaining == 0)
            return Result.Success<IReadOnlyList<ProviderInboundMessage>>([]);

        IReadOnlyList<SmsIrAccount> accounts = await _accounts.GetAccountsAsync(cancellationToken);
        List<ProviderInboundMessage> inbound = new List<ProviderInboundMessage>(remaining);

        foreach (SmsIrAccount account in accounts)
        {
            Result<SmsIrResponse<IReadOnlyList<SmsIrInboundData>>> response =
                await ExecuteAsync<IReadOnlyList<SmsIrInboundData>>(
                    () => CreateGetRequest(account, $"v1/receive/latest?count={remaining}"),
                    cancellationToken);

            if (response.IsFailure)
                return response.Error!;

            if (response.Value.Status != SmsIrStatusCodes.Success)
                return SmsIrProviderErrors.RequestStatus("receive/latest", response.Value);

            IReadOnlyList<SmsIrInboundData> entries = response.Value.Data ?? [];
            inbound.AddRange(entries.Take(remaining).Select(entry => new ProviderInboundMessage(
                entry.Mobile.ToString(CultureInfo.InvariantCulture),
                entry.Number.ToString(CultureInfo.InvariantCulture),
                entry.MessageText,
                entry.ReceivedDateTime.ToString(CultureInfo.InvariantCulture))));

            remaining -= Math.Min(entries.Count, remaining);
            if (remaining == 0)
                break;
        }

        return Result.Success<IReadOnlyList<ProviderInboundMessage>>(inbound);
    }

    private static HttpRequestMessage CreateSendRequest(
        SmsIrAccount account,
        long lineNumber,
        IReadOnlyList<ProviderSendRequest> requests)
    {
        SmsIrLikeToLikeRequest body = new SmsIrLikeToLikeRequest
        {
            LineNumber = lineNumber,
            MessageTexts = requests.Select(request => request.Body).ToArray(),
            Mobiles = requests.Select(request => request.MobileNumber).ToArray(),
            SendDateTime = null,
        };

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "v1/send/likeToLike")
        {
            Content = JsonContent.Create(body),
        };
        AddApiKey(request, account);
        return request;
    }

    private static HttpRequestMessage CreateGetRequest(SmsIrAccount account, string path)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
        AddApiKey(request, account);
        return request;
    }

    private static void AddApiKey(HttpRequestMessage request, SmsIrAccount account) =>
        request.Headers.Add("X-API-KEY", account.ApiKey);

    private async Task<Result<SmsIrResponse<T>>> ExecuteAsync<T>(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = createRequest();

        HttpResponseMessage response;
        try
        {
            await WaitForRequestSlotAsync(cancellationToken);
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Error.Provider(SmsIrErrorCodes.Transport, UserMessages.Providers.SmsIrTransport);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Provider(SmsIrErrorCodes.Timeout, UserMessages.Providers.SmsIrTimeout);
        }

        using (response)
        {
            SmsIrResponse<T>? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync<SmsIrResponse<T>>(cancellationToken);
            }
            catch (JsonException ex)
            {
                if (response.IsSuccessStatusCode)
                    return SmsIrProviderErrors.BadJson(ex.Message);
            }

            // sms.ir documents HTTP 400 for logical failures and keeps the native status in the
            // normal response envelope. Preserve that richer response whenever it is parseable.
            if (body is not null)
                return Result.Success(body);

            int statusCode = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return SmsIrProviderErrors.AuthenticationHttpStatus(statusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return SmsIrProviderErrors.RateLimitHttpStatus(statusCode);
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return SmsIrProviderErrors.BadRequestHttpStatus(statusCode);
            if (!response.IsSuccessStatusCode)
                return SmsIrProviderErrors.HttpStatus(statusCode);

            return Error.Provider(SmsIrErrorCodes.EmptyBody, UserMessages.Providers.SmsIrEmptyBody);
        }
    }

    private async Task WaitForRequestSlotAsync(CancellationToken cancellationToken)
    {
        if (_options.MinimumRequestInterval == TimeSpan.Zero)
            return;

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = _clock.GetUtcNow();
            TimeSpan delay = _nextRequestAtUtc - now;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _clock, cancellationToken);

            _nextRequestAtUtc = _clock.GetUtcNow() + _options.MinimumRequestInterval;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static Result<IReadOnlyList<Result<ProviderDispatchResult>>> MapSendHttpError(
        Error error,
        IReadOnlyCollection<ProviderSendRequest> requests)
    {
        if (SmsIrProviderErrors.TryGetHttpStatus(
                error,
                SmsIrErrorCodes.AuthenticationHttpStatus,
                out int authenticationStatus))
        {
            return Result.Success(WholeRequestOutcomes(
                requests,
                ProviderDispatchResult.DefinitelyNotSubmitted(
                    authenticationStatus,
                    UserMessages.Providers.SmsIrDefinitelyNotSubmitted(authenticationStatus))));
        }

        if (SmsIrProviderErrors.TryGetHttpStatus(error, SmsIrErrorCodes.RateLimitHttpStatus, out int rateLimitStatus))
        {
            return Result.Success(WholeRequestOutcomes(
                requests,
                ProviderDispatchResult.RetryableNotSubmitted(
                    rateLimitStatus,
                    UserMessages.Providers.SmsIrHttpStatus(rateLimitStatus))));
        }

        if (SmsIrProviderErrors.TryGetHttpStatus(error, SmsIrErrorCodes.BadRequestHttpStatus, out int badRequestStatus))
        {
            return Result.Success(WholeRequestOutcomes(
                requests,
                ProviderDispatchResult.Rejected(
                    badRequestStatus,
                    UserMessages.Providers.SmsIrHttpStatus(badRequestStatus))));
        }

        return error;
    }

    private static Result<IReadOnlyList<Result<ProviderDispatchResult>>> MapSendResponse(
        SmsIrResponse<SmsIrSendData> response,
        IReadOnlyList<ProviderSendRequest> requests)
    {
        if (response.Status == SmsIrStatusCodes.InsufficientCredit)
        {
            return Result.Success(WholeRequestOutcomes(
                requests,
                ProviderDispatchResult.InsufficientCredit(response.Status, response.Message)));
        }

        if (SmsIrStatusCodes.IsDefinitelyNotSubmitted(response.Status))
        {
            return Result.Success(WholeRequestOutcomes(
                requests,
                ProviderDispatchResult.DefinitelyNotSubmitted(
                    response.Status,
                    UserMessages.Providers.SmsIrDefinitelyNotSubmitted(response.Status))));
        }

        if (SmsIrStatusCodes.IsRetryableNotSubmitted(response.Status))
        {
            return Result.Success(WholeRequestOutcomes(
                requests,
                ProviderDispatchResult.RetryableNotSubmitted(
                    response.Status,
                    UserMessages.Providers.SmsIrRequestStatus("likeToLike", response.Status, response.Message))));
        }

        if (SmsIrStatusCodes.IsRejectedRequest(response.Status))
        {
            return Result.Success(WholeRequestOutcomes(
                requests,
                ProviderDispatchResult.Rejected(
                    response.Status,
                    UserMessages.Providers.SmsIrRejectedStatus(response.Status))));
        }

        if (response.Status != SmsIrStatusCodes.Success)
            return SmsIrProviderErrors.RequestStatus("likeToLike", response);

        IReadOnlyList<long?> messageIds = response.Data?.MessageIds ?? [];
        List<Result<ProviderDispatchResult>> results =
            new List<Result<ProviderDispatchResult>>(requests.Count);

        for (int i = 0; i < requests.Count; i++)
        {
            if (i >= messageIds.Count)
            {
                results.Add(Error.Provider(SmsIrErrorCodes.MissingResult, UserMessages.Providers.SmsIrMissingResult));
                continue;
            }

            long? messageId = messageIds[i];
            if (messageId is null)
            {
                results.Add(ProviderDispatchResult.Rejected(
                    resultCode: null,
                    UserMessages.Providers.SmsIrNullMessageId));
                continue;
            }

            if (messageId.Value == 0)
            {
                results.Add(ProviderDispatchResult.Rejected(
                    SmsIrStatusCodes.Blacklisted,
                    UserMessages.Providers.SmsIrRejectedStatus(SmsIrStatusCodes.Blacklisted)));
                continue;
            }

            results.Add(ProviderDispatchResult.Accepted(
                messageId.Value.ToString(CultureInfo.InvariantCulture),
                response.Status));
        }

        return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(results);
    }

    private static IReadOnlyList<Result<ProviderDispatchResult>> WholeRequestOutcomes(
        IReadOnlyCollection<ProviderSendRequest> requests,
        ProviderDispatchResult outcome) =>
        requests.Select(_ => Result.Success(outcome)).ToList();

    private sealed record SmsIrSendGroupKey(
        SmsIrAccount Account,
        string SenderLine,
        long LineNumber);
}
