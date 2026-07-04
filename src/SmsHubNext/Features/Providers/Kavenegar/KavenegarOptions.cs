namespace SmsHubNext.Features.Providers.Kavenegar;

/// <summary>
/// Connection settings for the Kavenegar REST provider.
/// </summary>
public sealed class KavenegarOptions
{
    public const string SectionName = "Providers:Kavenegar";
    public const int MaxMessagesPerRequest = 200;
    public const int MaxStatusesPerRequest = 500;

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://api.kavenegar.com";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; init; } = MaxMessagesPerRequest;

    public IReadOnlyList<KavenegarAccount> Accounts { get; init; } = [];

    public void Validate()
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException($"{SectionName}:BaseUrl is required when Kavenegar is enabled.");

        if (BatchSize is < 1 or > MaxMessagesPerRequest)
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and {MaxMessagesPerRequest}.");

        if (Accounts.Count == 0)
            throw new InvalidOperationException($"{SectionName}:Accounts must contain at least one account.");

        HashSet<string> seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Accounts.Count; i++)
        {
            KavenegarAccount account = Accounts[i];
            string at = $"{SectionName}:Accounts[{i}]";

            if (string.IsNullOrWhiteSpace(account.ApiKey))
                throw new InvalidOperationException($"{at}:ApiKey is required.");
            if (account.SenderLines.Count == 0)
                throw new InvalidOperationException($"{at}:SenderLines must list at least one sender line.");

            foreach (string line in account.SenderLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    throw new InvalidOperationException($"{at}:SenderLines contains a blank line number.");
                if (!seenLines.Add(line.Trim()))
                    throw new InvalidOperationException(
                        $"{SectionName}: sender line '{line}' is claimed by more than one account.");
            }
        }
    }
}
