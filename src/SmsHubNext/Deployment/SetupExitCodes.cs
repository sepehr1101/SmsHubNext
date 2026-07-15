namespace SmsHubNext.Deployment;

public static class SetupExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 2;
    public const int InvalidRequest = 3;
    public const int DatabaseConnectionFailed = 4;
    public const int SettingsWriteFailed = 5;
    public const int UnexpectedFailure = 10;
}
