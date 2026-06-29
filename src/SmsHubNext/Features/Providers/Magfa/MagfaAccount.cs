using System.Net.Http.Headers;
using System.Text;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// One Magfa service account and the sender lines it owns. Credentials are scoped to the account
/// (not the whole provider) so different sender lines can authenticate against different Magfa
/// accounts — the routing seam for "line 3000… is on account A, line 1000… is on account B".
/// The matching account is selected per outbound request from the message's sender line
/// (<see cref="MagfaAccountResolver"/>); account-scoped reads (statuses/mid/inbox) fan out over
/// every configured account.
/// </summary>
public sealed class MagfaAccount
{
    /// <summary>Account username (the part before the <c>/</c> in the Basic-auth user field).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Account domain (the part after the <c>/</c> in the Basic-auth user field).</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>Service password (distinct from the panel login password).</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// The sender lines (e.g. <c>30001234</c>) that authenticate with this account. A line maps to
    /// exactly one account; outbound messages on that line are sent with these credentials.
    /// </summary>
    public IReadOnlyList<string> SenderLines { get; init; } = [];

    /// <summary>The Basic-auth user field: <c>USERNAME/DOMAIN</c> (see the API reference, §3).</summary>
    public string BasicAuthUser => $"{Username}/{Domain}";

    /// <summary>
    /// The <c>Authorization</c> header for this account, built once from its credentials. Set per
    /// request (the typed client carries no default credential) so each account uses its own.
    /// </summary>
    public AuthenticationHeaderValue AuthorizationHeader => new(
        "Basic",
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{BasicAuthUser}:{Password}")));
}
