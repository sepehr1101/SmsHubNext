using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Time;

namespace SmsHubNext.Features.Batches;

public sealed class BrowseBatchesHandler(Db db)
{
    public const int PageSize = 25;

    public async Task<Result<BatchListPage>> Handle(BrowseBatchesRequest request, CancellationToken token)
    {
        Result validation = request.Validate();
        if (validation.IsFailure) return validation.Error!;
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        List<BatchListItem> rows = (await connection.QueryAsync<BatchListItem>(new CommandDefinition(
            BatchesSql.Browse,
            new
            {
                request.FromUtc, request.ToUtcExclusive,
                FromJalali = DateParameter(request.FromUtc),
                ToJalali = DateParameter(request.ToUtcExclusive.AddTicks(-1)),
                BeforeDate = new DbString { Value = request.Before?.SubmitDateJalali, IsAnsi = true, Length = 10 },
                BeforeId = request.Before?.Id,
                Take = PageSize + 1,
            }, cancellationToken: token))).AsList();
        bool hasMore = rows.Count > PageSize;
        if (hasMore) rows.RemoveAt(PageSize);
        return new BatchListPage(rows, hasMore ? new(rows[^1].SubmitDateJalali, rows[^1].Id) : null);
    }

    private static DbString DateParameter(DateTime utc) => new()
    {
        Value = JalaliDate.FromUtc(utc), IsAnsi = true, Length = 10,
    };
}
