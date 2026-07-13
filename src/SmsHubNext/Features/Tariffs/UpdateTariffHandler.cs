using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

public sealed class UpdateTariffHandler
{
    private readonly Db _db;

    public UpdateTariffHandler(Db db) => _db = db;

    public async Task<Result> Handle(int id, UpdateTariffRequest request, CancellationToken cancellationToken)
    {
        if (id <= 0)
            return Error.Validation("tariffs.invalid_id", UserMessages.Tariffs.InvalidId);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        using SqlTransaction transaction = connection.BeginTransaction();

        TariffLifecycleRow? tariff = await connection.QuerySingleOrDefaultAsync<TariffLifecycleRow>(new CommandDefinition(
            TariffsSql.GetLifecycle,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
        if (tariff is null)
            return Error.NotFound("tariffs.not_found", UserMessages.Tariffs.NotFound);

        Result validation = request.Validate(tariff.EffectiveFromUtc);
        if (validation.IsFailure)
            return validation.Error!;

        if (request.IsActive && await HasOverlap(connection, transaction, tariff, request, cancellationToken))
            return Error.Conflict("tariffs.effective_range_overlap", UserMessages.Tariffs.EffectiveRangeOverlap);

        await connection.ExecuteAsync(new CommandDefinition(
            TariffsSql.UpdateLifecycle,
            new { Id = id, request.EffectiveToUtc, request.IsActive },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return Result.Success();
    }

    private static async Task<bool> HasOverlap(
        SqlConnection connection,
        SqlTransaction transaction,
        TariffLifecycleRow tariff,
        UpdateTariffRequest request,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            TariffsSql.HasOverlappingTariff,
            new
            {
                tariff.ProviderId,
                tariff.MessageTypeId,
                Encoding = (byte)tariff.Encoding,
                tariff.EffectiveFromUtc,
                request.EffectiveToUtc,
                ExcludeId = tariff.Id,
            },
            transaction,
            cancellationToken: cancellationToken));
}
