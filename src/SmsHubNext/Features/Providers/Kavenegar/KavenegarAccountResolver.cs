namespace SmsHubNext.Features.Providers.Kavenegar;

public sealed class KavenegarAccountResolver
{
    private readonly Dictionary<string, KavenegarAccount> _bySenderLine;

    public KavenegarAccountResolver(KavenegarOptions options)
    {
        _bySenderLine = new Dictionary<string, KavenegarAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (KavenegarAccount account in options.Accounts)
        {
            foreach (string senderLine in account.SenderLines)
                _bySenderLine[senderLine] = account;
        }

        Accounts = options.Accounts;
    }

    public IReadOnlyList<KavenegarAccount> Accounts { get; }

    public KavenegarAccount? Resolve(string senderLine) =>
        _bySenderLine.TryGetValue(senderLine, out KavenegarAccount? account) ? account : null;
}
