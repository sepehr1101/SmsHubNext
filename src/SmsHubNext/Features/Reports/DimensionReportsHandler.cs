using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

public sealed class DimensionReportsHandler(Db db)
{
    public async Task<Result<DimensionReportOptions>> Options(CancellationToken token)
    {
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        using SqlMapper.GridReader reader = await connection.QueryMultipleAsync(new CommandDefinition(DimensionReportsSql.Options, cancellationToken: token));
        List<ReportFilterOption> customers = (await reader.ReadAsync<ReportFilterOption>()).ToList();
        List<ReportFilterOption> providers = (await reader.ReadAsync<ReportFilterOption>()).ToList();
        List<ReportFilterOption> types = (await reader.ReadAsync<ReportFilterOption>()).ToList();
        List<ReportFilterOption> geography = (await reader.ReadAsync<ReportFilterOption>()).ToList();
        return new DimensionReportOptions(customers, providers, types, geography);
    }

    public async Task<Result<IReadOnlyList<DimensionReportRow>>> Handle(DimensionReportRequest request, CancellationToken token)
    {
        Result validation = request.Validate();
        if (validation.IsFailure) return validation.Error!;
        MessageReportRequest filters = request.Filters;
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        IEnumerable<DimensionReportRow> rows = await connection.QueryAsync<DimensionReportRow>(new CommandDefinition(
            DimensionReportsSql.Build(request.Kind), new
            {
                filters.FromJalali, filters.ToJalali, filters.CustomerId, filters.ProviderId,
                filters.MessageTypeId, filters.GeoSectionId, request.GeographyLevel,
            }, cancellationToken: token));
        return Result.Success<IReadOnlyList<DimensionReportRow>>(rows.ToList());
    }
}

