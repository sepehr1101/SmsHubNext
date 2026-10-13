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
    // Only a one-recipient contract was supplied; do not infer an upstream batch limit/order guarantee.
    public int MaxBatchSize => 1;
    public bool SupportsIdempotentResend => false;

    public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
        IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>([]);
        if (requests.Count > MaxBatchSize)
            return KnownNotSent(requests, "تعداد پیام‌ها از حد پشتیبانی‌شده ارسال 4040 بیشتر است.");
        if (!await routes.UsesEsb(requests[0].SenderLine, cancellationToken))
            return KnownNotSent(requests, "مسیر ESB برای خط 4040 فعال نیست.");

        Result<string> token = await tokens.Get(cancellationToken);
        if (token.IsFailure)
            return KnownNotSent(requests, token.Error!.Message);

        using HttpRequestMessage request = new(HttpMethod.Post, options.SendUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new SendRequest(
            requests.Select(item => item.SenderLine).ToArray(),
            requests.Select(item => item.Body).ToArray(),
            requests.Select(item => LocalRecipient(item.MobileNumber)).ToArray(),
            requests.Select(item => item.MessageId.ToString(CultureInfo.InvariantCulture)).ToArray()));
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(requests.Select(_ =>
                    Result.Success(ProviderDispatchResult.DefinitelyNotSubmitted((int)response.StatusCode,
                        "احراز هویت ارسال در ESB پذیرفته نشد."))).ToArray());
            }
            if (!response.IsSuccessStatusCode)
                return Error.Provider("4040.send_http", "پاسخ HTTP ارسال 4040 ناموفق است؛ نتیجه ارسال مشخص نیست.");
            SendResponse? body = await response.Content.ReadFromJsonAsync<SendResponse>(cancellationToken);
            return MapResponse(body, requests.Count);
        }
        catch (HttpRequestException)
        {
            return Error.Provider("4040.send_transport", "ارتباط ارسال 4040 قطع شد؛ نتیجه ارسال مشخص نیست.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Provider("4040.send_timeout", "مهلت ارسال 4040 پایان یافت؛ نتیجه ارسال مشخص نیست.");
        }
        catch (JsonException)
        {
            return Error.Provider("4040.send_json", "پاسخ ارسال 4040 معتبر نیست؛ نتیجه ارسال مشخص نیست.");
        }
    }

    private static Result<IReadOnlyList<Result<ProviderDispatchResult>>> MapResponse(SendResponse? response, int count)
    {
        SendData? data = response?.Result?.Data;
        if (response?.Status?.StatusCode != 200 || response.Result?.Status?.StatusCode != 200 ||
            data?.Status != 0 || data.SmsIds is null || data.SmsIds.Length != count ||
            data.SmsIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 50 || id.Any(c => !char.IsAscii(c))) ||
            data.SmsIds.Distinct(StringComparer.Ordinal).Count() != count)
        {
            // No documented rejection/error taxonomy: never refund or resend based on guessed codes.
            return Error.Provider("4040.send_response", "پاسخ ارسال 4040 قابل تطبیق با پیام‌ها نیست؛ نتیجه ارسال مشخص نیست.");
        }
        return Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(data.SmsIds
            .Select(id => Result.Success(ProviderDispatchResult.Accepted(id, 0))).ToArray());
    }

    private static Result<IReadOnlyList<Result<ProviderDispatchResult>>> KnownNotSent(
        IReadOnlyList<ProviderSendRequest> requests, string detail) =>
        Result.Success<IReadOnlyList<Result<ProviderDispatchResult>>>(requests.Select(_ =>
            Result.Success(ProviderDispatchResult.RetryableNotSubmitted(null, detail))).ToArray());

    private static string LocalRecipient(string number) =>
        number.Length == 12 && number.StartsWith("989", StringComparison.Ordinal) ? "0" + number[2..] : number;

    public Task<Result<string?>> ResolveSubmittedMessageIdAsync(long messageId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<string?>(null));

    public async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        List<ProviderDeliveryReport> reports = new(providerMessageIds.Count);
        // No delivery batch limit has been supplied. Query each distinct ID separately.
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
        Result<string> token = await tokens.Get(cancellationToken);
        if (token.IsFailure)
            return token.Error!;
        using HttpRequestMessage request = new(HttpMethod.Post, options.DeliveryUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Required by the gateway bug; this is a placeholder, not another credential.
        request.Headers.Add("basicAuthorization", "Basic bugfix");
        request.Content = JsonContent.Create(new DeliveryRequest([id]));
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Error.Provider("4040.delivery_http", "استعلام تحویل 4040 ناموفق بود.");
            DeliveryResponse? body = await response.Content.ReadFromJsonAsync<DeliveryResponse>(cancellationToken);
            return MapDelivery(body, id);
        }
        catch (HttpRequestException)
        {
            return Error.Provider("4040.delivery_transport", "ارتباط با سرویس وضعیت تحویل 4040 برقرار نشد.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Provider("4040.delivery_timeout", "مهلت استعلام تحویل 4040 پایان یافت.");
        }
        catch (JsonException)
        {
            return Error.Provider("4040.delivery_json", "پاسخ وضعیت تحویل 4040 معتبر نیست.");
        }
    }

    private Result<ProviderDeliveryReport> MapDelivery(DeliveryResponse? response, string id)
    {
        DeliveryData? data = response?.Result?.Data;
        if (response?.Status?.StatusCode != 200 || response.Result?.Status?.StatusCode != 200 ||
            data?.Status != 0 || data.Dlrs is null)
            return Error.Provider("4040.delivery_response", "پاسخ وضعیت تحویل 4040 معتبر نیست.");
        // Correlate by the complete smsId dictionary key, never the unrelated returned uid.
        if (!data.Dlrs.TryGetValue(id, out DeliveryEntry? entry))
            return new ProviderDeliveryReport(id, null, 0);
        if (entry?.Dlr is not int raw)
            return Error.Provider("4040.delivery_response", "کد وضعیت تحویل 4040 در پاسخ وجود ندارد.");
        DeliveryReportStatus? status = options.TerminalDeliveryStatuses.TryGetValue(raw, out DeliveryReportStatus terminal)
            ? terminal : null;
        return new ProviderDeliveryReport(id, status, raw);
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
    private sealed record SendData(int? Status, [property: JsonPropertyName("smsids")] string[]? SmsIds);
    private sealed record DeliveryRequest([property: JsonPropertyName("_body")] string[] Body);
    private sealed record DeliveryResponse(DeliveryResult? Result, ResponseStatus? Status);
    private sealed record DeliveryResult(DeliveryData? Data, ResponseStatus? Status);
    private sealed record DeliveryData(int? Status, Dictionary<string, DeliveryEntry?>? Dlrs);
    private sealed record DeliveryEntry(int? Dlr, long? DateDlr, long? DateSent, string? Uid);
}
