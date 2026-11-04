using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

public sealed class DailyResendHandler(Db db, TimeProvider clock)
{
    public async Task<Result<DailyResendSummary>> Begin(string day, ApiKeyIdentity identity, CancellationToken token)
    {
        if (identity.AccessLevel != ApiKeyAccessLevel.Administrator) return Forbidden();
        Result<string> parsed = DailyResendDate.Parse(day, clock.GetUtcNow().UtcDateTime);
        if (parsed.IsFailure) return parsed.Error!;
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        string resource = Resource(parsed.Value);
        if (!await ResendLock.TryAcquire(connection, resource, 10000, token)) return Busy();
        try
        {
            Guid? existing = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT Id FROM dbo.DailyResendOperation WHERE SubmitDateJalali=@Day;", new { Day = parsed.Value }, cancellationToken: token));
            Guid id = existing ?? Guid.NewGuid();
            if (existing is null)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT dbo.DailyResendOperation(Id,SubmitDateJalali,CutoffMessageId,State,CreatedByApiKeyId,CreatedAtUtc)
                    SELECT @Id,@Day,COALESCE(MAX(Id),0),0,@ApiKeyId,@Now
                    FROM dbo.Message WHERE SubmitDateJalali=@Day;
                    """, new { Id = id, Day = parsed.Value, identity.ApiKeyId, Now = clock.GetUtcNow().UtcDateTime }, cancellationToken: token));
            }
            return await Read(connection, id, token);
        }
        finally { await ResendLock.Release(connection, resource); }
    }

    public async Task<Result<DailyResendSummary>> Status(Guid id, ApiKeyIdentity identity, CancellationToken token)
    {
        if (identity.AccessLevel != ApiKeyAccessLevel.Administrator) return Forbidden();
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        return await Read(connection, id, token);
    }

    public async Task<Result<DailyResendSummary>> Confirm(Guid id, ApiKeyIdentity identity, CancellationToken token)
    {
        if (identity.AccessLevel != ApiKeyAccessLevel.Administrator) return Forbidden();
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        Result<DailyResendSummary> current = await Read(connection, id, token);
        if (current.IsFailure) return current.Error!;
        string resource = Resource(current.Value.SubmitDateJalali);
        if (!await ResendLock.TryAcquire(connection, resource, 10000, token)) return Busy();
        try
        {
            // Repeated confirmation never adds members, resets counters or creates a new operation.
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.DailyResendOperation SET State=2, ConfirmedByApiKeyId=@ApiKeyId, ConfirmedAtUtc=@Now
                WHERE Id=@Id AND State IN (1,4);
                """, new { Id = id, identity.ApiKeyId, Now = clock.GetUtcNow().UtcDateTime }, cancellationToken: token));
            return await Read(connection, id, token);
        }
        finally { await ResendLock.Release(connection, resource); }
    }

    private static async Task<Result<DailyResendSummary>> Read(SqlConnection connection, Guid id, CancellationToken token)
    {
        DailyResendSummary? summary = await connection.QuerySingleOrDefaultAsync<DailyResendSummary>(
            new CommandDefinition(DailyResendSql.Summary, new { Id = id }, cancellationToken: token));
        if (summary is null) return Error.NotFound("sending.daily_not_found", "عملیات ارسال مجدد یافت نشد.");
        List<DailyResendSkipReason> reasons = (await connection.QueryAsync<DailyResendSkipReason>(new CommandDefinition("""
            SELECT COALESCE(SkipCode,'sending.daily_validation_failed') Code,COUNT_BIG(*) Count
            FROM dbo.DailyResendItem WHERE OperationId=@Id AND State=2
            GROUP BY SkipCode ORDER BY COUNT_BIG(*) DESC,SkipCode;
            """, new { Id = id }, cancellationToken: token))).AsList();
        return summary with { SkipReasons = reasons };
    }

    internal static string Resource(string day) => "SmsHubNext.DailyResend:" + day;
    private static Error Forbidden() => Error.Forbidden("sending.daily_forbidden", "ارسال مجدد روزانه فقط از پنل مدیریت مجاز است.");
    private static Error Busy() => Error.Conflict("sending.daily_busy", "این روز در حال پردازش است؛ دوباره تلاش کنید.");
}
