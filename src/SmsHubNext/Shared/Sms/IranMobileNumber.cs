namespace SmsHubNext.Shared.Sms;

public static class IranMobileNumber
{
    public static string Normalize(string value)
    {
        string trimmed = value.Trim();
        return trimmed.StartsWith("09", StringComparison.Ordinal) ? "98" + trimmed[1..] : trimmed;
    }
}
