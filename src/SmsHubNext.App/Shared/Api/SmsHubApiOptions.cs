namespace SmsHubNext.App.Shared.Api;

public sealed class SmsHubApiOptions
{
    public const string SectionName = "SmsHubApi";

    public string BaseUrl { get; set; } = "http://localhost:5080";
    public string AdminApiKey { get; set; } = string.Empty;
    public string CustomerApiKey { get; set; } = string.Empty;
}
