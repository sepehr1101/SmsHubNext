using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

internal static class SenderLineRouteRules
{
    public static async Task<Result> Validate(SqlConnection connection, byte providerId, bool useEsb, CancellationToken token)
    {
        if (!useEsb)
            return Result.Success();
        string? code = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            SenderLinesSql.GetProviderCode, new { ProviderId = providerId }, cancellationToken: token));
        return string.Equals(code, ProviderCodes.Provider4040, StringComparison.OrdinalIgnoreCase)
            ? Result.Success()
            : Error.Validation("sender_lines.esb_provider_unsupported", "مسیر ESB فقط برای ارائه‌دهنده 4040 پشتیبانی می‌شود.");
    }
}
