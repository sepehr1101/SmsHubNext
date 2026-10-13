using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Providers.Esb;

/// <summary>OAuth client credentials for the ESB gateway; independent of provider credentials.</summary>
public sealed class EsbOptions
{
    public const string SectionName = "Esb";
    public const string HttpClientName = "Esb";

    public bool Enabled { get; init; }
    public string LoginUrl { get; init; } = "https://esb.abfaisfahan.com:8243/token";
    public string SendUrl { get; init; } = "https://esb.abfaisfahan.com:8243/Nww-SendSms/1.0";
    public string DeliveryUrl { get; init; } = "https://esb.abfaisfahan.com:8243/Nww-CheckDeliverySms/1.0";
    /// <summary>Only documented terminal codes belong here; other codes remain pending.</summary>
    public Dictionary<int, DeliveryReportStatus> TerminalDeliveryStatuses { get; init; } = new();
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (!Enabled)
            return;
        if (!IsHttps(LoginUrl) || !IsHttps(SendUrl) || !IsHttps(DeliveryUrl))
            throw new InvalidOperationException("Esb:LoginUrl, Esb:SendUrl and Esb:DeliveryUrl must be absolute HTTPS URLs.");
        if (TerminalDeliveryStatuses.Values.Any(status => !Enum.IsDefined(status)))
            throw new InvalidOperationException("Esb:TerminalDeliveryStatuses must contain valid normalized delivery statuses.");
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password) || Username.Contains(':'))
            throw new InvalidOperationException("Esb:Username and Esb:Password must contain valid Basic authentication credentials.");
        if (Timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Esb:Timeout must be positive.");
    }

    private static bool IsHttps(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
}
