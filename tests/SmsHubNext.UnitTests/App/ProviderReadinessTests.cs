using SmsHubNext.Features.Dashboard.Ui;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class ProviderReadinessTests
{
    [Theory]
    [InlineData(true, true, true, "4040", true)]
    [InlineData(false, true, true, "4040", false)]
    [InlineData(true, false, true, "4040", false)]
    [InlineData(true, true, false, "4040", false)]
    [InlineData(true, true, true, "magfa", false)]
    public void Esb_readiness_requires_enabled_gateway_and_active_4040_line_and_provider(
        bool enabled, bool activeLine, bool activeProvider, string code, bool ready)
    {
        Provider provider = new(1, "سرویس", code, activeProvider);
        SenderLine line = new(1, 1, "40401403", true, null, null, activeLine, UseEsb: true);
        Assert.Equal(ready, ProviderReadiness.IsLineReady(line, provider, [], enabled));
    }

    [Theory]
    [InlineData(true, true, 1, true)]
    [InlineData(false, true, 1, false)]
    [InlineData(true, false, 1, false)]
    [InlineData(true, true, 2, false)]
    public void A_linked_account_is_not_ready_if_inactive_missing_secret_or_from_another_provider(
        bool active, bool secret, byte accountProvider, bool ready)
    {
        Provider provider = new(1, "مگفا", "magfa", true);
        SenderLine line = new(1, 1, "30001234", true, null, 10, true);
        ProviderAccount account = new(10, accountProvider, "magfa", "test", "test", new Dictionary<string, string>(), secret,
            active, DateTime.UnixEpoch, DateTime.UnixEpoch);
        Assert.Equal(ready, ProviderReadiness.IsLineReady(line, provider, [account], false));
    }
}
