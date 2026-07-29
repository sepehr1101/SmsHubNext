namespace SmsHubNext.Deployment;

public sealed class DatabaseSetupRequest
{
    public string Server { get; init; } = string.Empty;

    public string Database { get; init; } = string.Empty;

    public DatabaseAuthenticationMode Authentication { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public int ConnectTimeoutSeconds { get; init; } = 15;

    public bool TrustServerCertificate { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        ValidateRequiredValue(Server, nameof(Server), 256, errors);
        ValidateRequiredValue(Database, nameof(Database), 128, errors);

        if (!Enum.IsDefined(Authentication))
            errors.Add("روش احراز هویت باید Windows یا SqlServer باشد.");

        if (ConnectTimeoutSeconds is < 1 or > 120)
            errors.Add("مهلت اتصال باید بین ۱ تا ۱۲۰ ثانیه باشد.");

        if (Authentication == DatabaseAuthenticationMode.SqlServer)
        {
            ValidateRequiredValue(Username, nameof(Username), 128, errors);
            ValidateRequiredValue(Password, nameof(Password), 512, errors);
        }

        return errors;
    }

    private static void ValidateRequiredValue(
        string? value,
        string name,
        int maximumLength,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"مقدار {name} الزامی است.");
            return;
        }

        if (value.Length > maximumLength)
            errors.Add($"مقدار {name} نباید بیشتر از {maximumLength} نویسه باشد.");

        if (value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal))
            errors.Add($"مقدار {name} نباید چندخطی باشد.");
    }
}
