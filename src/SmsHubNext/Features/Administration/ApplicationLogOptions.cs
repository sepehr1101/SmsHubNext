namespace SmsHubNext.Features.Administration;

public sealed class ApplicationLogOptions
{
    public const string SectionName = "ApplicationLogs";
    public string? DirectoryPath { get; set; }

    public string ResolveDirectory(IHostEnvironment environment) =>
        Path.GetFullPath(DirectoryPath ?? (environment.IsProduction() && OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SmsHubNext", "Logs")
            : Path.Combine(environment.ContentRootPath, "App_Data", "Logs")), environment.ContentRootPath);
}
