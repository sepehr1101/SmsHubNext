using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Authentication.Ui;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Shared.Ui;

/// <summary>
/// The UI boundary: authenticate the administrator session and resolve the configured
/// key on every operation, preserving expiry, revocation, CIDR checks and audit attribution.
/// Feature handlers are the same ones used by the external API.
/// </summary>
public sealed class UiOperations(
    AuthenticationStateProvider authenticationState,
    ApiKeyAuthenticator authenticator,
    IOptions<ManagementUiOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<UiOperations> logger)
{
    private readonly IPAddress? _remoteIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;

    public async Task<UiResult<T>> Run<T>(
        Func<ApiKeyIdentity, CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken,
        bool customerKey = false)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.OperationTimeoutSeconds, 1, 300)));
        try
        {
            AuthenticationState state = await authenticationState.GetAuthenticationStateAsync();
            if (!state.User.Identities.Any(identity =>
                    identity.IsAuthenticated && identity.AuthenticationType == DevelopmentAccount.AuthenticationScheme) ||
                !state.User.IsInRole(DevelopmentAccount.AdministratorRole))
            {
                return UiResult<T>.Failure("برای انجام این عملیات وارد پنل مدیریت شوید.");
            }

            string key = customerKey ? options.Value.CustomerApiKey : options.Value.AdminApiKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                return UiResult<T>.Failure(customerKey
                    ? "کلید ارسال مشتری در ManagementUi:CustomerApiKey مشخص نشده است."
                    : "کلید مدیریتی در ManagementUi:AdminApiKey مشخص نشده است.");
            }

            Result<ApiKeyIdentity> authentication = await authenticator.Authenticate(key, _remoteIp, deadline.Token);
            if (authentication.IsFailure)
                return UiResult<T>.Failure(authentication.Error!.Message);

            ApiKeyAccessLevel expected = customerKey ? ApiKeyAccessLevel.Customer : ApiKeyAccessLevel.Administrator;
            if (authentication.Value.AccessLevel != expected)
                return UiResult<T>.Failure("سطح دسترسی کلید با این عملیات سازگار نیست.");

            Result<T> result = await operation(authentication.Value, deadline.Token);
            return result.IsSuccess
                ? UiResult<T>.Success(result.Value)
                : UiResult<T>.Failure(result.Error!.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UiResult<T>.Failure("درخواست لغو شد.");
        }
        catch (OperationCanceledException)
        {
            return UiResult<T>.Failure("زمان انتظار برای انجام عملیات به پایان رسید.");
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Management UI database operation failed");
            return UiResult<T>.Failure("عملیات پایگاه داده انجام نشد. جزئیات در گزارش خطای سامانه ثبت شده است.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Management UI operation failed");
            return UiResult<T>.Failure("انجام عملیات ناموفق بود. جزئیات در گزارش خطای سامانه ثبت شده است.");
        }
    }

    public Task<UiResult<string?>> Run(
        Func<ApiKeyIdentity, CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken) =>
        Run<string?>(async (identity, token) =>
        {
            Result result = await operation(identity, token);
            return result.IsSuccess ? Result.Success<string?>(null) : result.Error!;
        }, cancellationToken);
}
