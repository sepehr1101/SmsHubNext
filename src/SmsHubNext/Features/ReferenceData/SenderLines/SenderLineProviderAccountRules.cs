using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

internal static class SenderLineProviderAccountRules
{
    public static async Task<Result> Validate(
        SqlConnection connection,
        byte senderLineProviderId,
        int providerAccountId,
        CancellationToken cancellationToken)
    {
        ProviderAccountBinding? account = await connection.QuerySingleOrDefaultAsync<ProviderAccountBinding>(
            new CommandDefinition(
                SenderLinesSql.GetProviderAccountBinding,
                new { ProviderAccountId = providerAccountId },
                cancellationToken: cancellationToken));

        if (account is null)
            return Error.Validation("sender_lines.unknown_provider_account", UserMessages.ReferenceData.SenderLineUnknownProviderAccount);

        if (account.ProviderId != senderLineProviderId)
            return Error.Validation("sender_lines.provider_account_provider_mismatch", UserMessages.ReferenceData.ProviderAccountProviderMismatch);

        if (!account.IsActive)
            return Error.Validation("sender_lines.inactive_provider_account", UserMessages.ReferenceData.InactiveProviderAccount);

        return Result.Success();
    }
}

internal sealed record ProviderAccountBinding(byte ProviderId, bool IsActive);

internal sealed record SenderLineBinding(short Id, byte ProviderId);
