using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Setup;

public sealed class FactoryResetHandler
{
    private readonly Db _db;
    private readonly TimeProvider _clock;

    public FactoryResetHandler(Db db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<FactoryResetResponse>> Handle(
        FactoryResetRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        using SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            await DatabaseApplicationLock.AcquireFactoryResetExclusiveAsync(
                connection,
                transaction,
                cancellationToken);

            bool messagesExist = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                FactoryResetSql.MessagesExist,
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (messagesExist)
            {
                transaction.Rollback();
                return Error.Conflict(
                    "setup.factory_reset_messages_exist",
                    UserMessages.Setup.FactoryResetMessagesExist);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                FactoryResetSql.ResetDatabase,
                transaction: transaction,
                commandTimeout: 60,
                cancellationToken: cancellationToken));

            transaction.Commit();
            return new FactoryResetResponse(
                _clock.GetUtcNow().UtcDateTime,
                RequiresSetupWizard: true);
        }
        catch
        {
            if (transaction.Connection is not null)
                transaction.Rollback();

            throw;
        }
    }
}
