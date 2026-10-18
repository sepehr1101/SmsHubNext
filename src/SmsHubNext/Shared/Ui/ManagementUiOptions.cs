namespace SmsHubNext.Shared.Ui;

public sealed class ManagementUiOptions
{
    public const string SectionName = "ManagementUi";

    public string AdminApiKey { get; set; } = string.Empty;
    public string CustomerApiKey { get; set; } = string.Empty;
    public int OperationTimeoutSeconds { get; set; } = 15;
}
