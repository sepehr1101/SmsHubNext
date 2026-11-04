using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmsHubNext.Features.Providers.Esb;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.Provider4040;

/// <summary>4040's ESB send and delivery contracts, sharing one OAuth token cache.</summary>
public sealed class Provider4040SmsProvider(
    HttpClient client, EsbOptions options, EsbAccessTokenProvider tokens,
    Provider4040RouteResolver routes) : ISmsProvider
{
    public string Name => ProviderCodes.Provider4040;
    // The dispatcher splits larger batches into upstream requests of at most 100 messages.
    public int MaxBatchSize => 100;
    public bool SupportsIdempotentResend => true;
    public TimeSpan? UncertainResendWindow => TimeSpan.FromHours(24);

    public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>([]);
        if (requests.Count > MaxBatchSize)
            return KnownNotSent(requests, EsbFailure.Create("4040.batch_limit", "تعداد پیام‌ها از حد پشتیبانی‌شده ارسال 4040 بیشتر است.", FailureStage.Validation, FailureOrigin.Application));
        if (!await routes.UsesEsb(requests[0].SenderLine, cancellationToken))
            return KnownNotSent(requests, EsbFailure.Create("4040.route_disabled", "مسیر ESB برای خط 4040 فعال نیست.", FailureStage.Validation, FailureOrigin.Application));

        Result<string> token = await tokens.Get(cancellationToken);
        if (token.IsFailure)
            return KnownNotSent(requests, token.Error!);

        using HttpRequestMessage request = new(HttpMethod.Post, options.SendUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new SendRequest(
            requests.Select(item => item.SenderLine).ToArray(),
            requests.Select(item => item.Body).ToArray(),
            requests.Select(item => LocalRecipient(item.MobileNumber)).ToArray(),
            requests.Select(item => item.MessageId.ToString(CultureInfo.InvariantCulture)).ToArray()));
        int? httpStatus = null;
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            httpStatus = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await tokens.Invalidate(token.Value, cancellationToken);
                // Let the dispatcher retry with its persisted attempt budget and original UID.
                return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(requests.Select(_ =>
                    Result.Success(ProviderDispatchResult.RetryableNotSubmitted((int)response.StatusCode,
                        "توکن ارسال ESB پذیرفته نشد؛ تلاش بعدی با توکن تازه انجام می‌شود.")
                    with { Diagnostic = new(FailureOrigin.EsbGateway, FailureStage.Submission, "4040.send_unauthorized",
                        FailureImpact.NotSubmitted, FailureAction.RetryAutomatically, HttpStatus: 401) })).ToArray());
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(requests.Select(_ =>
                    Result.Success(ProviderDispatchResult.DefinitelyNotSubmitted((int)response.StatusCode,
                        "احراز هویت ارسال در ESB پذیرفته نشد.")
                    with { Diagnostic = new(FailureOrigin.EsbGateway, FailureStage.Submission, "4040.send_forbidden",
                        FailureImpact.NotSubmitted, FailureAction.FixConfiguration, HttpStatus: 403) })).ToArray());
            }
            if (!response.IsSuccessStatusCode)
                return EsbFailure.Create("4040.send_http", "پاسخ HTTP ارسال 4040 ناموفق است؛ نتیجه ارسال مشخص نیست.", FailureStage.Submission, httpStatus: (int)response.StatusCode);
            SendResponse? body = await response.Content.ReadFromJsonAsync<SendResponse>(cancellationToken);
            return MapResponse(body, requests.Count, (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return EsbFailure.Create("4040.send_transport", "ارتباط ارسال 4040 قطع شد؛ نتیجه ارسال مشخص نیست.", FailureStage.Submission, FailureOrigin.Network);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EsbFailure.Create("4040.send_timeout", "مهلت ارسال 4040 پایان یافت؛ نتیجه ارسال مشخص نیست.", FailureStage.Submission, FailureOrigin.Network);
        }
        catch (JsonException)
        {
            return EsbFailure.Create("4040.send_json", "پاسخ ارسال 4040 معتبر نیست؛ نتیجه ارسال مشخص نیست.", FailureStage.Submission, httpStatus: httpStatus);
        }
    }

    private static Result<IReadOnlyList<Result<ProviderDispatchResult>>> MapResponse(SendResponse? response, int count, int httpStatus)
    {
        SendData? data = response?.Result?.Data;
        if (response?.Status?.StatusCode != 200 || response.Result?.Status?.StatusCode != 200 ||
            data?.Status != 0 || data.SmsIds is null || data.SmsIds.Length != count ||
            data.SmsIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 50 || id.Any(c => !char.IsAscii(c))) ||
            data.SmsIds.Distinct(StringComparer.Ordinal).Count() != count)
        {
            // No documented rejection/error taxonomy: never refund or resend based on guessed codes.
            return EsbFailure.Create("4040.send_response", "پاسخ ارسال 4040 قابل تطبیق با پیام‌ها نیست؛ نتیجه ارسال مشخص نیست.", FailureStage.Submission,
                ResponseOrigin(response?.Status?.StatusCode, response?.Result?.Status?.StatusCode, data?.Status),
                httpStatus: httpStatus, upstreamStatus: FailedStatus(response?.Status?.StatusCode, response?.Result?.Status?.StatusCode), businessCode: data?.Status);
        }
        return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(data.SmsIds
            .Select(id => Result.Success(ProviderDispatchResult.Accepted(id, 0))).ToArray());
    }

    private static Result<IReadOnlyList<Result<ProviderDispatchResult>>> KnownNotSent(
        IReadOnlyList<ProviderSendRequest> requests, Error error) =>
        Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(requests.Select(_ =>
            Result.Success(ProviderDispatchResult.RetryableNotSubmitted(null, error.Message)
                with { Diagnostic = error.Diagnostic })).ToArray());

    private static int? FailedStatus(int? outer, int? inner) => outer != 200 ? outer : inner != 200 ? inner : null;

    private static FailureOrigin ResponseOrigin(int? outer, int? inner, int? business) =>
        outer == 200 && inner == 200 && business is not null and not 0
            ? FailureOrigin.SmsProvider : FailureOrigin.EsbGateway;

    private static string LocalRecipient(string number) =>
        number.Length == 12 && number.StartsWith("989", StringComparison.Ordinal) ? "0" + number[2..] : number;

    public Task<Result<string?>> ResolveSubmittedMessageIdAsync(long messageId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<string?>(null));

    public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken) =>
        options.UseBatchDeliveryQueries
            ? GetDeliveryReportsBatchAsync(providerMessageIds, cancellationToken)
            : GetDeliveryReportsIndividuallyAsync(providerMessageIds, cancellationToken);

    private async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsBatchAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        List<ProviderDeliveryReport> reports = new(providerMessageIds.Count);
        foreach (string[] ids in providerMessageIds.Distinct(StringComparer.Ordinal).Chunk(options.DeliveryQueryBatchSize))
        {
            Result<IReadOnlyList<ProviderDeliveryReport>> batch = await QueryDeliveryBatch(ids, cancellationToken);
            if (batch.IsFailure)
                return batch.Error!;
            reports.AddRange(batch.Value);
        }
        return Result.Success<IReadOnlyList<ProviderDeliveryReport>>(reports);
    }

    private async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsIndividuallyAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        List<ProviderDeliveryReport> reports = new(providerMessageIds.Count);
        // Retain the original single-ID polling path for configuration-based rollback.
        foreach (string id in providerMessageIds.Distinct(StringComparer.Ordinal))
        {
            Result<ProviderDeliveryReport> report = await QueryDelivery(id, cancellationToken);
            if (report.IsFailure)
                return report.Error!;
            reports.Add(report.Value);
        }
        return Result.Success<IReadOnlyList<ProviderDeliveryReport>>(reports);
    }

    private async Task<Result<ProviderDeliveryReport>> QueryDelivery(string id, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ProviderDeliveryReport>> result = await QueryDeliveryBatch([id], cancellationToken);
        if (result.IsFailure)
            return result.Error!;
        return result.Value[0];
    }

    private async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> QueryDeliveryBatch(
        string[] ids, CancellationToken cancellationToken)
    {
        Result<string> token = await tokens.Get(cancellationToken);
        if (token.IsFailure)
            return token.Error! with { Diagnostic = token.Error.Diagnostic is null ? null
                : token.Error.Diagnostic with { Impact = FailureImpact.DeliveryQueryUnavailable } };
        using HttpRequestMessage request = new(HttpMethod.Post, options.DeliveryUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Required by the gateway bug; this is a placeholder, not another credential.
        request.Headers.Add("basicAuthorization", "Basic bugfix");
        request.Content = JsonContent.Create(new DeliveryRequest(ids));
        int? httpStatus = null;
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            httpStatus = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                await tokens.Invalidate(token.Value, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return EsbFailure.Create("4040.delivery_http", "استعلام تحویل 4040 ناموفق بود.", FailureStage.DeliveryQuery, httpStatus: (int)response.StatusCode);
            DeliveryResponse? body = await response.Content.ReadFromJsonAsync<DeliveryResponse>(cancellationToken);
            return MapDeliveryBatch(body, ids, (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return EsbFailure.Create("4040.delivery_transport", "ارتباط با سرویس وضعیت تحویل 4040 برقرار نشد.", FailureStage.DeliveryQuery, FailureOrigin.Network);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EsbFailure.Create("4040.delivery_timeout", "مهلت استعلام تحویل 4040 پایان یافت.", FailureStage.DeliveryQuery, FailureOrigin.Network);
        }
        catch (JsonException)
        {
            return EsbFailure.Create("4040.delivery_json", "پاسخ وضعیت تحویل 4040 معتبر نیست.", FailureStage.DeliveryQuery, httpStatus: httpStatus);
        }
    }

    private Result<IReadOnlyList<ProviderDeliveryReport>> MapDeliveryBatch(DeliveryResponse? response, string[] ids, int httpStatus)
    {
        DeliveryData? data = response?.Result?.Data;
        if (response?.Status?.StatusCode != 200 || response.Result?.Status?.StatusCode != 200 ||
            data?.Status != 0 || data.Dlrs is null)
            return EsbFailure.Create("4040.delivery_response", "پاسخ وضعیت تحویل 4040 معتبر نیست.", FailureStage.DeliveryQuery,
                ResponseOrigin(response?.Status?.StatusCode, response?.Result?.Status?.StatusCode, data?.Status),
                httpStatus: httpStatus, upstreamStatus: FailedStatus(response?.Status?.StatusCode, response?.Result?.Status?.StatusCode), businessCode: data?.Status);
        List<ProviderDeliveryReport> reports = new(ids.Length);
        foreach (string id in ids)
        {
            Result<ProviderDeliveryReport> report = MapDelivery(data, id);
            if (report.IsFailure)
                return report.Error!;
            reports.Add(report.Value);
        }
        return Result.Success<IReadOnlyList<ProviderDeliveryReport>>(reports);
    }

    private Result<ProviderDeliveryReport> MapDelivery(DeliveryData data, string id)
    {
        // Correlate by the complete smsId dictionary key, never the unrelated returned uid.
        if (!data.Dlrs!.TryGetValue(id, out DeliveryEntry? entry))
            return new ProviderDeliveryReport(id, null, 0) { HasProviderStatus = false };
        if (entry?.Dlr is not int raw)
            return EsbFailure.Create("4040.delivery_response", "کد وضعیت تحویل 4040 در پاسخ وجود ندارد.", FailureStage.DeliveryQuery, httpStatus: 200);
        DeliveryReportStatus? status = options.TerminalDeliveryStatuses.TryGetValue(raw, out DeliveryReportStatus terminal)
            ? terminal : null;
        return new ProviderDeliveryReport(id, status, raw)
        {
            ProviderSentAtRaw = entry.DateSent?.ToString(),
            ProviderDlrAtRaw = entry.DateDlr?.ToString(),
        };
    }

    public Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
        int maxCount, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<IReadOnlyList<ProviderInboundMessage>>(
            Error.Provider("4040.inbound_not_configured", "قرارداد دریافت پیام 4040 هنوز مشخص نشده است.")));

    private sealed record SendRequest(
        [property: JsonPropertyName("senders")] string[] Senders,
        [property: JsonPropertyName("messages")] string[] Messages,
        [property: JsonPropertyName("recipients")] string[] Recipients,
        [property: JsonPropertyName("uids")] string[] Uids);
    private sealed record SendResponse(ResponseResult? Result, ResponseStatus? Status);
    private sealed record ResponseResult(SendData? Data, ResponseStatus? Status);
    private sealed record ResponseStatus(int StatusCode);
    private sealed record SendData([property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int? Status, [property: JsonPropertyName("smsids")] string[]? SmsIds);
    private sealed record DeliveryRequest([property: JsonPropertyName("_body")] string[] Body);
    private sealed record DeliveryResponse(DeliveryResult? Result, ResponseStatus? Status);
    private sealed record DeliveryResult(DeliveryData? Data, ResponseStatus? Status);
    private sealed record DeliveryData([property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int? Status, Dictionary<string, DeliveryEntry?>? Dlrs);
    private sealed record DeliveryEntry(
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int? Dlr,
        JsonElement? DateDlr, JsonElement? DateSent, string? Uid);
}
