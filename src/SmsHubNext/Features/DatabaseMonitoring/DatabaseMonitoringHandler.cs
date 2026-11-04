using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DatabaseMonitoring;

public sealed class DatabaseMonitoringHandler(Db db, TimeProvider clock, DatabaseMonitoringCollectorState collector)
{
    public async Task<Result<DatabaseMonitorReport>> Read(DatabaseMonitorSection section, CancellationToken token, int sort = 2)
    {
        if (!Enum.IsDefined(section) || sort is < 1 or > 3)
            return Error.Validation("database_monitor.invalid_section", "بخش پایش یا ترتیب معتبر نیست.");
        string sql = section switch
        {
            DatabaseMonitorSection.Overview => DatabaseMonitoringSql.Overview,
            DatabaseMonitorSection.Tables => DatabaseMonitoringSql.Tables,
            DatabaseMonitorSection.Indexes => DatabaseMonitoringSql.Indexes,
            DatabaseMonitorSection.Statistics => DatabaseMonitoringSql.Statistics,
            DatabaseMonitorSection.Requests => DatabaseMonitoringSql.Requests,
            DatabaseMonitorSection.Queries => DatabaseMonitoringSql.Queries.Replace("ORDER BY Metric2 DESC", $"ORDER BY Metric{sort} DESC", StringComparison.Ordinal),
            DatabaseMonitorSection.Operations => DatabaseMonitoringSql.Operations,
            DatabaseMonitorSection.History => DatabaseMonitoringSql.History,
            _ => DatabaseMonitoringSql.Alerts,
        };
        try
        {
            await using SqlConnection connection = await db.OpenConnectionAsync(token);
            if (section is DatabaseMonitorSection.Requests or DatabaseMonitorSection.Alerts)
            {
                bool visible = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(DatabaseMonitoringSql.ServerVisibility,cancellationToken:token));
                if (!visible) return Error.Forbidden("database_monitor.visibility", "دسترسی مشاهده نشست‌های دیگر کافی نیست؛ فهرست خالی به معنی نبود درخواست یا هشدار نیست. راهنمای دسترسی را ببینید.");
            }
            if (section == DatabaseMonitorSection.Queries)
            {
                string state = await connection.QuerySingleAsync<string>(new CommandDefinition(DatabaseMonitoringSql.QueryStoreState, cancellationToken: token));
                if (state is not ("READ_WRITE" or "READ_ONLY"))
                    return new DatabaseMonitorReport([], clock.GetUtcNow().UtcDateTime, "Query Store فعال نیست؛ تغییری در تنظیمات SQL Server اعمال نشده است.");
            }
            IEnumerable<DatabaseReading> rows = await connection.QueryAsync<DatabaseReading>(new CommandDefinition(sql, commandTimeout: 15, cancellationToken: token));
            return new DatabaseMonitorReport(rows.ToList(), clock.GetUtcNow().UtcDateTime, collector.Status.Error);
        }
        catch (SqlException exception) when (exception.IsMonitoringPermissionDenied())
        {
            return Error.Forbidden("database_monitor.permission", "دسترسی خواندن اطلاعات پایش کافی نیست. راهنمای دسترسی همین صفحه را ببینید؛ هیچ دسترسی به‌طور خودکار اعطا نمی‌شود.");
        }
        catch (SqlException exception) when (exception.IsCommandTimeout())
        {
            return Error.Conflict("database_monitor.timeout", "بررسی در مهلت تعیین‌شده کامل نشد. در زمان خلوت‌تر دوباره بررسی کنید.");
        }
    }

    public async Task<Result<DatabaseMonitorReport>> InspectIndex(int objectId, int indexId, int partition, CancellationToken token)
    {
        if (objectId <= 0 || indexId <= 0 || partition <= 0)
            return Error.Validation("database_monitor.invalid_index", "ایندکس و پارتیشن معتبر انتخاب کنید.");
        try
        {
            await using SqlConnection connection = await db.OpenConnectionAsync(token);
            bool exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                DatabaseMonitoringSql.IndexExists,
                new { ObjectId = objectId, IndexId = indexId, PartitionNumber = partition }, cancellationToken: token));
            if (!exists) return Error.NotFound("database_monitor.index_missing", "ایندکس یا پارتیشن انتخاب‌شده دیگر موجود نیست.");
            IEnumerable<DatabaseReading> rows = await connection.QueryAsync<DatabaseReading>(new CommandDefinition(DatabaseMonitoringSql.IndexDetail,
                new { ObjectId = objectId, IndexId = indexId, PartitionNumber = partition }, commandTimeout: 15, cancellationToken: token));
            return new DatabaseMonitorReport(rows.ToList(), clock.GetUtcNow().UtcDateTime, collector.Status.Error);
        }
        catch (SqlException exception) when (exception.IsMonitoringPermissionDenied())
        {
            return Error.Forbidden("database_monitor.permission", "دسترسی بررسی ایندکس کافی نیست.");
        }
        catch (SqlException exception) when (exception.IsCommandTimeout())
        {
            return Error.Conflict("database_monitor.timeout", "بررسی نمونه‌ای به پایان نرسید؛ در زمان خلوت‌تر بررسی کنید.");
        }
    }

    public async Task<Result<IReadOnlyList<DatabaseGrowthPoint>>> Growth(CancellationToken token)
    {
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        IEnumerable<DatabaseGrowthPoint> rows = await connection.QueryAsync<DatabaseGrowthPoint>(new CommandDefinition(DatabaseMonitoringSql.Growth, cancellationToken: token));
        return Result.Success<IReadOnlyList<DatabaseGrowthPoint>>(rows.ToList());
    }

    public async Task Collect(CancellationToken token)
    {
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        await connection.ExecuteAsync(new CommandDefinition(DatabaseMonitoringSql.Snapshot, new { Now = now }, commandTimeout: 15, cancellationToken: token));
        await connection.ExecuteAsync(new CommandDefinition(DatabaseMonitoringSql.Observe, new { Now = now }, commandTimeout: 15, cancellationToken: token));
    }
}


