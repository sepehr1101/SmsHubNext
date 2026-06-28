using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

/// <summary>Reads tariffs with their price bands.</summary>
public sealed class ListTariffsHandler
{
    private readonly Db _db;

    public ListTariffsHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<TariffResponse>>> Handle(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        List<Tariff> tariffs = (await connection.QueryAsync<Tariff>(
            new CommandDefinition(TariffsSql.ListTariffs, cancellationToken: cancellationToken))).AsList();

        IEnumerable<TariffRate> rates = await connection.QueryAsync<TariffRate>(
            new CommandDefinition(TariffsSql.ListRates, cancellationToken: cancellationToken));

        Dictionary<int, IReadOnlyList<TariffRate>> ratesByTariff = rates
            .GroupBy(rate => rate.TariffId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TariffRate>)group.ToList());

        IReadOnlyList<TariffResponse> response = tariffs
            .Select(tariff => new TariffResponse(
                tariff.Id,
                tariff.ProviderId,
                tariff.MessageTypeId,
                tariff.Encoding,
                tariff.EffectiveFromUtc,
                tariff.EffectiveToUtc,
                tariff.Currency,
                tariff.IsActive,
                ratesByTariff.TryGetValue(tariff.Id, out IReadOnlyList<TariffRate>? tariffRates) ? tariffRates : Array.Empty<TariffRate>()))
            .ToList();

        return Result.Success(response);
    }
}
