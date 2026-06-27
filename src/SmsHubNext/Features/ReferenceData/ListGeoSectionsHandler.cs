using Dapper;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

/// <summary>Reads the geographic sections (provinces, cities, zones).</summary>
public sealed class ListGeoSectionsHandler
{
    private readonly Db _db;

    public ListGeoSectionsHandler(Db db) => _db = db;

    public async Task<Result<IReadOnlyList<GeoSection>>> Handle(CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<GeoSection>(
            new CommandDefinition(GeoSectionsSql.List, cancellationToken: cancellationToken));

        IReadOnlyList<GeoSection> sections = rows.AsList();
        return Result.Success(sections);
    }
}
