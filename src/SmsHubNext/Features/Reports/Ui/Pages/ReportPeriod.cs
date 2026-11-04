using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Reports.Ui.Pages;

public sealed record ReportPeriod(string From, string To)
{
    public static ReportPeriod Current(TimeProvider clock, string period)
    {
        string today = PersianDisplay.SearchText(PersianDisplay.Date(IranTime.FromUtc(clock.GetUtcNow()), "yyyy/MM/dd"));
        return new(period switch { "today" => today, "year" => today[..4] + "/01/01", _ => today[..7] + "/01" }, today);
    }
}
