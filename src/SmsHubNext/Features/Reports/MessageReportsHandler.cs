using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

public sealed class MessageReportsHandler
{
    private readonly Db _db;

    public MessageReportsHandler(Db db) => _db = db;

    public async Task<Result<MessageReportSummary>> Summary(
        MessageReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        MessageStatsRow row = await connection.QuerySingleAsync<MessageStatsRow>(new CommandDefinition(
            ReportsSql.Summary,
            request,
            cancellationToken: cancellationToken));

        return new MessageReportSummary(row.ToStats());
    }

    public async Task<Result<IReadOnlyList<ProviderMessageReportRow>>> ByProvider(
        MessageReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        IEnumerable<ProviderStatsRow> rows = await connection.QueryAsync<ProviderStatsRow>(new CommandDefinition(
            ReportsSql.ByProvider,
            request,
            cancellationToken: cancellationToken));

        IReadOnlyList<ProviderMessageReportRow> result = rows
            .Select(row => new ProviderMessageReportRow(
                row.ProviderId,
                row.ProviderCode,
                row.ProviderName,
                row.ToStats()))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<MessageTypeReportRow>>> ByMessageType(
        MessageReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        IEnumerable<MessageTypeStatsRow> rows = await connection.QueryAsync<MessageTypeStatsRow>(new CommandDefinition(
            ReportsSql.ByMessageType,
            request,
            cancellationToken: cancellationToken));

        IReadOnlyList<MessageTypeReportRow> result = rows
            .Select(row => new MessageTypeReportRow(
                row.MessageTypeId,
                row.MessageTypeCode,
                row.MessageTypeName,
                row.ToStats()))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<GeoMessageReportRow>>> ByGeo(
        MessageReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        IEnumerable<GeoStatsRow> rows = await connection.QueryAsync<GeoStatsRow>(new CommandDefinition(
            ReportsSql.ByGeo,
            request,
            cancellationToken: cancellationToken));

        IReadOnlyList<GeoMessageReportRow> result = rows
            .Select(row => new GeoMessageReportRow(
                row.GeoSectionId,
                row.SectionType,
                row.GeoSectionCode,
                row.GeoSectionName,
                row.ToStats()))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<JalaliMonthMessageReportRow>>> ByJalaliMonth(
        MessageReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        IEnumerable<JalaliMonthStatsRow> rows = await connection.QueryAsync<JalaliMonthStatsRow>(new CommandDefinition(
            ReportsSql.ByJalaliMonth,
            request,
            cancellationToken: cancellationToken));

        IReadOnlyList<JalaliMonthMessageReportRow> result = rows
            .Select(row => new JalaliMonthMessageReportRow(row.JalaliMonth, row.ToStats()))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<GeoMessageReportRow>>> ByGeoRollup(
        MessageReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        IEnumerable<GeoStatsRow> rows = await connection.QueryAsync<GeoStatsRow>(new CommandDefinition(
            ReportsSql.ByGeoRollup,
            request,
            cancellationToken: cancellationToken));

        IReadOnlyList<GeoMessageReportRow> result = rows
            .Select(row => new GeoMessageReportRow(
                row.GeoSectionId,
                row.SectionType,
                row.GeoSectionCode,
                row.GeoSectionName,
                row.ToStats()))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<ProviderMessageTypeGeoReportRow>>> ByProviderMessageTypeGeo(
        MessageReportRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        IEnumerable<ProviderMessageTypeGeoStatsRow> rows =
            await connection.QueryAsync<ProviderMessageTypeGeoStatsRow>(new CommandDefinition(
                ReportsSql.ByProviderMessageTypeGeo,
                request,
                cancellationToken: cancellationToken));

        IReadOnlyList<ProviderMessageTypeGeoReportRow> result = rows
            .Select(row => new ProviderMessageTypeGeoReportRow(
                row.ProviderId,
                row.ProviderCode,
                row.ProviderName,
                row.MessageTypeId,
                row.MessageTypeCode,
                row.MessageTypeName,
                row.GeoSectionId,
                row.SectionType,
                row.GeoSectionCode,
                row.GeoSectionName,
                row.ToStats()))
            .ToList();

        return Result.Success(result);
    }

    private record MessageStatsRow(
        long MessageCount,
        long SegmentCount,
        decimal TotalCost,
        long PendingCount,
        long DeliveredCount,
        long UndeliveredCount,
        long ExpiredCount,
        long UnknownCount,
        decimal DeliveryRate)
    {
        public MessageReportStats ToStats() => new(
            MessageCount,
            SegmentCount,
            TotalCost,
            PendingCount,
            DeliveredCount,
            UndeliveredCount,
            ExpiredCount,
            UnknownCount,
            DeliveryRate);
    }

    private sealed record ProviderStatsRow(
        byte ProviderId,
        string ProviderCode,
        string ProviderName,
        long MessageCount,
        long SegmentCount,
        decimal TotalCost,
        long PendingCount,
        long DeliveredCount,
        long UndeliveredCount,
        long ExpiredCount,
        long UnknownCount,
        decimal DeliveryRate) : MessageStatsRow(
            MessageCount,
            SegmentCount,
            TotalCost,
            PendingCount,
            DeliveredCount,
            UndeliveredCount,
            ExpiredCount,
            UnknownCount,
            DeliveryRate);

    private sealed record MessageTypeStatsRow(
        byte MessageTypeId,
        string MessageTypeCode,
        string MessageTypeName,
        long MessageCount,
        long SegmentCount,
        decimal TotalCost,
        long PendingCount,
        long DeliveredCount,
        long UndeliveredCount,
        long ExpiredCount,
        long UnknownCount,
        decimal DeliveryRate) : MessageStatsRow(
            MessageCount,
            SegmentCount,
            TotalCost,
            PendingCount,
            DeliveredCount,
            UndeliveredCount,
            ExpiredCount,
            UnknownCount,
            DeliveryRate);

    private sealed record GeoStatsRow(
        int? GeoSectionId,
        byte? SectionType,
        string? GeoSectionCode,
        string GeoSectionName,
        long MessageCount,
        long SegmentCount,
        decimal TotalCost,
        long PendingCount,
        long DeliveredCount,
        long UndeliveredCount,
        long ExpiredCount,
        long UnknownCount,
        decimal DeliveryRate) : MessageStatsRow(
            MessageCount,
            SegmentCount,
            TotalCost,
            PendingCount,
            DeliveredCount,
            UndeliveredCount,
            ExpiredCount,
            UnknownCount,
            DeliveryRate);

    private sealed record JalaliMonthStatsRow(
        string JalaliMonth,
        long MessageCount,
        long SegmentCount,
        decimal TotalCost,
        long PendingCount,
        long DeliveredCount,
        long UndeliveredCount,
        long ExpiredCount,
        long UnknownCount,
        decimal DeliveryRate) : MessageStatsRow(
            MessageCount,
            SegmentCount,
            TotalCost,
            PendingCount,
            DeliveredCount,
            UndeliveredCount,
            ExpiredCount,
            UnknownCount,
            DeliveryRate);

    private sealed record ProviderMessageTypeGeoStatsRow(
        byte ProviderId,
        string ProviderCode,
        string ProviderName,
        byte MessageTypeId,
        string MessageTypeCode,
        string MessageTypeName,
        int? GeoSectionId,
        byte? SectionType,
        string? GeoSectionCode,
        string GeoSectionName,
        long MessageCount,
        long SegmentCount,
        decimal TotalCost,
        long PendingCount,
        long DeliveredCount,
        long UndeliveredCount,
        long ExpiredCount,
        long UnknownCount,
        decimal DeliveryRate) : MessageStatsRow(
            MessageCount,
            SegmentCount,
            TotalCost,
            PendingCount,
            DeliveredCount,
            UndeliveredCount,
            ExpiredCount,
            UnknownCount,
            DeliveryRate);
}
