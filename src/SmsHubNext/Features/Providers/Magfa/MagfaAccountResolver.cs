namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// Maps a sender line to the <see cref="MagfaAccount"/> that owns it, built once from
/// <see cref="MagfaOptions.Accounts"/>. <see cref="MagfaSmsProvider"/> uses it to pick per-request
/// credentials on send (by the message's sender line) and exposes <see cref="Accounts"/> for the
/// account-scoped reads (statuses/mid/inbox) that have no per-message line to key on.
/// </summary>
public sealed class MagfaAccountResolver
{
    private readonly Dictionary<string, MagfaAccount> _bySenderLine;

    public MagfaAccountResolver(MagfaOptions options)
    {
        Accounts = options.Accounts;

        _bySenderLine = new Dictionary<string, MagfaAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (MagfaAccount account in options.Accounts)
        {
            foreach (string line in account.SenderLines)
                _bySenderLine[line.Trim()] = account;
        }
    }

    /// <summary>Every configured account, for the reads that fan out over all of them.</summary>
    public IReadOnlyList<MagfaAccount> Accounts { get; }

    /// <summary>The account that owns <paramref name="senderLine"/>, or <c>null</c> if none does.</summary>
    public MagfaAccount? Resolve(string senderLine) =>
        _bySenderLine.TryGetValue(senderLine, out MagfaAccount? account) ? account : null;
}
