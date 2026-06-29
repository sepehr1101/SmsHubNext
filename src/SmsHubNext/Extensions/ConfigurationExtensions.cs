namespace SmsHubNext.Extensions;

/// <summary>
/// Configuration sources beyond the host defaults — the configuration half of the composition root.
/// Keeps <c>Program.cs</c> minimal (see ARCHITECTURE.md §3).
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Layers an optional, gitignored <c>appsettings.{Environment}.local.json</c> on top of the
    /// committed settings. Local secrets (e.g. Magfa account credentials for local testing) live
    /// here and never reach source control — <c>appsettings.json</c> carries placeholders only.
    /// Loaded last so it overrides both <c>appsettings.json</c> and <c>appsettings.{Environment}.json</c>.
    /// </summary>
    public static WebApplicationBuilder AddLocalConfiguration(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile(
            $"appsettings.{builder.Environment.EnvironmentName}.local.json",
            optional: true,
            reloadOnChange: true);

        return builder;
    }
}
