using SmsHubNext.Shared.Ui;
using MessageReportSummary = SmsHubNext.Features.Reports.MessageReportSummary;
using ProviderMessageReportRow = SmsHubNext.Features.Reports.ProviderMessageReportRow;
using MessageTypeReportRow = SmsHubNext.Features.Reports.MessageTypeReportRow;
using JalaliMonthMessageReportRow = SmsHubNext.Features.Reports.JalaliMonthMessageReportRow;

namespace SmsHubNext.Features.Reports.Ui;

public sealed class ReportsService(
    UiOperations operations,
    MessageReportsHandler messageReportsHandler)
{
    public Task<UiResult<MessageReportSummary>> Summary(MessageReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageReportsHandler.Summary(request, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<ProviderMessageReportRow>>> ByProvider(MessageReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageReportsHandler.ByProvider(request, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<MessageTypeReportRow>>> ByMessageType(MessageReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageReportsHandler.ByMessageType(request, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<JalaliMonthMessageReportRow>>> ByMonth(MessageReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageReportsHandler.ByJalaliMonth(request, cancellationToken), token);

}
