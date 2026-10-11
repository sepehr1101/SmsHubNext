using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace SmsHubNext.App.Shared.Api;

public sealed class ManagementApiClient
{
    private readonly HttpClient _httpClient;
    private readonly SmsHubApiOptions _options;
    private readonly ManagementAccessTokenProvider _tokenProvider;

    public ManagementApiClient(
        HttpClient httpClient,
        IOptions<SmsHubApiOptions> options,
        ManagementAccessTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _tokenProvider = tokenProvider;
    }

    public Task<ApiClientResult<T>> Get<T>(string path, CancellationToken cancellationToken) =>
        Send<T>(HttpMethod.Get, path, null, cancellationToken);

    public Task<ApiClientResult<TResponse>> Post<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken) =>
        Send<TResponse>(HttpMethod.Post, path, request, cancellationToken);

    public async Task<ApiClientResult<TResponse>> PostWithCustomerApiKey<TRequest, TResponse>(
        string path,
        TRequest requestBody,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CustomerApiKey))
            return ApiClientResult<TResponse>.Failure("کلید ارسال مشتری در تنظیمات SmsHubApi:CustomerApiKey مشخص نشده است.");

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, BuildUri(path));
            request.Headers.Add("X-Api-Key", _options.CustomerApiKey);
            request.Content = JsonContent.Create(requestBody);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            ApiEnvelope<TResponse>? envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<TResponse>>(cancellationToken);
            return response.IsSuccessStatusCode && envelope?.Success == true
                ? ApiClientResult<TResponse>.Success(envelope.Data)
                : ApiClientResult<TResponse>.Failure(envelope?.Message ?? $"API request failed ({(int)response.StatusCode}).");
        }
        catch (HttpRequestException)
        {
            return ApiClientResult<TResponse>.Failure("ارتباط با API برقرار نشد. ابتدا پروژه SmsHubNext را اجرا کنید.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApiClientResult<TResponse>.Failure("درخواست لغو شد.");
        }
    }

    public Task<ApiClientResult<string?>> Put<TRequest>(
        string path,
        TRequest request,
        CancellationToken cancellationToken) =>
        Send<string?>(HttpMethod.Put, path, request, cancellationToken);

    public Task<ApiClientResult<string?>> Delete(string path, CancellationToken cancellationToken) =>
        Send<string?>(HttpMethod.Delete, path, null, cancellationToken);

    private async Task<ApiClientResult<T>> Send<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        string? configurationError = ValidateConfiguration();
        if (configurationError is not null)
            return ApiClientResult<T>.Failure(configurationError);

        try
        {
            string token = await _tokenProvider.Get(cancellationToken);
            using HttpRequestMessage request = new(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
                request.Content = JsonContent.Create(body);

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            ApiEnvelope<T>? envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(cancellationToken);
            if (response.IsSuccessStatusCode && envelope?.Success == true)
                return ApiClientResult<T>.Success(envelope.Data);

            return ApiClientResult<T>.Failure(envelope?.Message ?? $"API request failed ({(int)response.StatusCode}).");
        }
        catch (HttpRequestException)
        {
            return ApiClientResult<T>.Failure("ارتباط با API برقرار نشد. ابتدا پروژه SmsHubNext را اجرا کنید.");
        }
        catch (InvalidOperationException exception)
        {
            return ApiClientResult<T>.Failure(exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApiClientResult<T>.Failure("درخواست لغو شد.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiClientResult<T>.Failure("زمان انتظار برای پاسخ API به پایان رسید.");
        }
    }

    private string? ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            return "آدرس API در تنظیمات SmsHubApi:BaseUrl مشخص نشده است.";
        if (string.IsNullOrWhiteSpace(_options.AdminApiKey))
            return "برای اتصال، کلید مدیریتی را در SmsHubApi:AdminApiKey یا User Secrets وارد کنید.";
        return null;
    }

    private Uri BuildUri(string path) => new(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

}
