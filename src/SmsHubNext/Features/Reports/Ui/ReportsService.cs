using SmsHubNext.Shared.Ui;
using MessageReportSummary = SmsHubNext.Features.Reports.MessageReportSummary;
using ProviderMessageReportRow = SmsHubNext.Features.Reports.ProviderMessageReportRow;
using MessageTypeReportRow = SmsHubNext.Features.Reports.MessageTypeReportRow;
using JalaliMonthMessageReportRow = SmsHubNext.Features.Reports.JalaliMonthMessageReportRow;

namespace SmsHubNext.Features.Reports.Ui;

public sealed class ReportsService(
    UiOperations operations,
    MessageReportsHandler messageReportsHandler,
    DimensionReportsHandler dimensionReportsHandler,
    SearchMessagesHandler searchMessagesHandler,
    MessageDiagnosticsHandler messageDiagnosticsHandler)
{
    public Task<UiResult<DimensionReportOptions>> DimensionOptions(CancellationToken token) =>
        operations.Run((identity, cancellationToken) => dimensionReportsHandler.Options(cancellationToken), token);

    public Task<UiResult<IReadOnlyList<DimensionReportRow>>> Dimensions(DimensionReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => dimensionReportsHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<MessageDiagnostics>> Diagnostics(long messageId, bool live, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageDiagnosticsHandler.Handle(messageId, live, cancellationToken), token);

    public Task<UiResult<MessageSearchPage>> Search(SearchMessagesRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => searchMessagesHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<MessageReportSummary>> Summary(MessageReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageReportsHandler.Summary(request, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<ProviderMessageReportRow>>> ByProvider(MessageReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageReportsHandler.ByProvider(request, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<MessageTypeReportRow>>> ByMessageType(MessageReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageReportsHandler.ByMessageType(request, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<JalaliMonthMessageReportRow>>> ByMonth(MessageReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => messageReportsHandler.ByJalaliMonth(request, cancellationToken), token);

}
