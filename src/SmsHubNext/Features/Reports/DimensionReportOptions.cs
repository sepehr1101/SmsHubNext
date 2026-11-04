namespace SmsHubNext.Features.Reports;

public sealed record ReportFilterOption(int Id, string Name);
public sealed record DimensionReportOptions(
    IReadOnlyList<ReportFilterOption> Customers,
    IReadOnlyList<ReportFilterOption> Providers,
    IReadOnlyList<ReportFilterOption> MessageTypes,
    IReadOnlyList<ReportFilterOption> Geography);
