namespace SmsHubNext.Shared.Http;

/// <summary>Cross-origin browser access configured from the <c>Cors</c> section.</summary>
public sealed class ApplicationCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "ConfiguredOrigins";

    public bool Enabled { get; init; }
    public string[] AllowedOrigins { get; init; } = [];
    public string[] AllowedMethods { get; init; } = ["GET", "POST", "PUT", "DELETE"];
    public string[] AllowedHeaders { get; init; } = ["Accept", "Authorization", "Content-Type", "X-Api-Key"];
    public bool AllowCredentials { get; init; }
    public int PreflightMaxAgeSeconds { get; init; } = 600;

    public void Validate()
    {
        if (!Enabled)
            return;

        if (AllowedOrigins.Length == 0)
            throw new InvalidOperationException($"{SectionName}:AllowedOrigins must contain at least one origin when CORS is enabled.");

        foreach (string origin in AllowedOrigins)
            ValidateOrigin(origin);

        if (AllowedMethods.Length == 0 || AllowedMethods.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"{SectionName}:AllowedMethods must contain valid HTTP methods.");

        if (AllowedHeaders.Length == 0 || AllowedHeaders.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"{SectionName}:AllowedHeaders must contain valid HTTP header names.");

        if (PreflightMaxAgeSeconds < 0 || PreflightMaxAgeSeconds > 86400)
            throw new InvalidOperationException($"{SectionName}:PreflightMaxAgeSeconds must be between 0 and 86400.");
    }

    private static void ValidateOrigin(string origin)
    {
        string normalized = origin.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "*")
            throw new InvalidOperationException($"{SectionName}:AllowedOrigins must list explicit origins; wildcard '*' is not allowed.");

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.GetLeftPart(UriPartial.Authority) != normalized)
        {
            throw new InvalidOperationException(
                $"{SectionName}:AllowedOrigins entry '{origin}' must be an HTTP(S) origin without a path, query, or fragment.");
        }
    }
}
