using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;

namespace SmsHubNext.Features.Dashboard.Ui;

public static class ProviderReadiness
{
    public static bool IsLineReady(SenderLine line, Provider provider,
        IReadOnlyList<ProviderAccount> accounts, bool esbEnabled) =>
        provider.IsActive && line.IsActive && line.ProviderId == provider.Id &&
        (line.UseEsb
            ? provider.Code == "4040" && esbEnabled
            : accounts.Any(account => account.Id == line.ProviderAccountId && account.ProviderId == provider.Id && account.IsActive && account.HasSecret));
}
