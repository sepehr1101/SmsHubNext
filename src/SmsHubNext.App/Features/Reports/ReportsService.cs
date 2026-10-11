using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.Reports;

public sealed class ReportsService
{
    private readonly ManagementApiClient _api;
    public ReportsService(ManagementApiClient api) => _api = api;

    public Task<ApiClientResult<MessageReportSummary>> Summary(string query, CancellationToken token) => _api.Get<MessageReportSummary>($"reports/messages/summary?{query}", token);
    public Task<ApiClientResult<IReadOnlyList<ProviderMessageReportRow>>> ByProvider(string query, CancellationToken token) => _api.Get<IReadOnlyList<ProviderMessageReportRow>>($"reports/messages/by-provider?{query}", token);
    public Task<ApiClientResult<IReadOnlyList<MessageTypeReportRow>>> ByMessageType(string query, CancellationToken token) => _api.Get<IReadOnlyList<MessageTypeReportRow>>($"reports/messages/by-message-type?{query}", token);
    public Task<ApiClientResult<IReadOnlyList<JalaliMonthMessageReportRow>>> ByMonth(string query, CancellationToken token) => _api.Get<IReadOnlyList<JalaliMonthMessageReportRow>>($"reports/messages/by-jalali-month?{query}", token);
}
