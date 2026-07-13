using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

public sealed class CreateTariffHandler
{
    private readonly Db _db;

    public CreateTariffHandler(Db db) => _db = db;

    public async Task<Result<CreateTariffResponse>> Handle(
        CreateTariffRequest request,
        CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
        using SqlTransaction transaction = connection.BeginTransaction();

        Result references = await ValidateReferences(connection, transaction, request, cancellationToken);
        if (references.IsFailure)
            return references.Error!;

        if (request.IsActive && await HasOverlap(connection, transaction, request, null, cancellationToken))
            return Error.Conflict("tariffs.effective_range_overlap", UserMessages.Tariffs.EffectiveRangeOverlap);

        int tariffId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            TariffsSql.InsertTariff,
            new
            {
                request.ProviderId,
                request.MessageTypeId,
                Encoding = (byte)request.Encoding,
                request.EffectiveFromUtc,
                request.EffectiveToUtc,
                request.IsActive,
            },
            transaction,
            cancellationToken: cancellationToken));

        IEnumerable<object> rates = request.Rates
            .OrderBy(rate => rate.MinChars)
            .Select(rate => (object)new
            {
                TariffId = tariffId,
                rate.MinChars,
                rate.MaxChars,
                rate.PricePerSegment,
            });

        await connection.ExecuteAsync(new CommandDefinition(
            TariffsSql.InsertRate,
            rates,
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return new CreateTariffResponse(tariffId);
    }

    private static async Task<Result> ValidateReferences(
        SqlConnection connection,
        SqlTransaction transaction,
        CreateTariffRequest request,
        CancellationToken cancellationToken)
    {
        TariffReferenceRow references = await connection.QuerySingleAsync<TariffReferenceRow>(new CommandDefinition(
            TariffsSql.ValidateReferences,
            new { request.ProviderId, request.MessageTypeId },
            transaction,
            cancellationToken: cancellationToken));

        if (!references.ProviderExists)
            return Error.Validation("tariffs.unknown_provider", UserMessages.Tariffs.UnknownProvider);

        if (!references.MessageTypeExists)
            return Error.Validation("tariffs.unknown_message_type", UserMessages.Tariffs.UnknownMessageType);

        return Result.Success();
    }

    internal static async Task<bool> HasOverlap(
        SqlConnection connection,
        SqlTransaction transaction,
        CreateTariffRequest request,
        int? excludeId,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            TariffsSql.HasOverlappingTariff,
            new
            {
                request.ProviderId,
                request.MessageTypeId,
                Encoding = (byte)request.Encoding,
                request.EffectiveFromUtc,
                request.EffectiveToUtc,
                ExcludeId = excludeId,
            },
            transaction,
            cancellationToken: cancellationToken));
}

internal sealed record TariffReferenceRow(bool ProviderExists, bool MessageTypeExists);
