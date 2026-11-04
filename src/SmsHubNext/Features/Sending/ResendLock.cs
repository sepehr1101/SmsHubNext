using Dapper;
using Microsoft.Data.SqlClient;

namespace SmsHubNext.Features.Sending;

/// <summary>Session-scoped so the normal acceptance transaction can keep owning billing and writes.</summary>
internal static class ResendLock
{
    public static async Task<bool> TryAcquire(SqlConnection connection, string resource, int timeout, CancellationToken token)
    {
        int result = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @result INT;
            EXEC @result = sys.sp_getapplock @Resource=@Resource, @LockMode='Exclusive',
                @LockOwner='Session', @LockTimeout=@Timeout;
            SELECT @result;
            """, new { Resource = resource, Timeout = timeout }, cancellationToken: token));
        return result >= 0;
    }

    public static Task Release(SqlConnection connection, string resource) => connection.ExecuteAsync(new CommandDefinition(
        "EXEC sys.sp_releaseapplock @Resource=@Resource, @LockOwner='Session';", new { Resource = resource }));
}
