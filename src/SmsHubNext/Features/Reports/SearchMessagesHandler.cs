using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Features.Authentication;
using DNTPersianUtils.Core;

namespace SmsHubNext.Features.Reports;

public sealed class SearchMessagesHandler(Db db)
{
    public const int PageSize = 25;

    public Task<Result<MessageSearchPage>> HandleForCustomer(SearchMessagesRequest request, ApiKeyIdentity identity, CancellationToken token)
    {
        if (identity.AccessLevel != ApiKeyAccessLevel.Customer || identity.CustomerId <= 0)
            return Task.FromResult(Result.Failure<MessageSearchPage>(Error.Forbidden(
                "reports.customer_key_required", "برای مشاهدهٔ پیام‌ها از کلید API مشتری استفاده کنید.")));
        return Handle(request with { CustomerId = identity.CustomerId }, token);
    }

    public async Task<Result<MessageSearchPage>> Handle(SearchMessagesRequest request, CancellationToken token)
    {
        SearchMessagesRequest query = request.Normalize();
        Result validation = query.Validate();
        if (validation.IsFailure) return validation.Error!;
        (DateTime fromUtc, DateTime untilUtc) = query.UtcRange();

        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        List<MessageSearchRow> rows = (await connection.QueryAsync<MessageSearchRow>(new CommandDefinition(
            MessageSearchSql.Search,
            new
            {
                query.CustomerId,
                FromJalali = Ansi(fromUtc.ToPersianDateTimeString("yyyy/MM/dd", false), 10),
                ToJalali = Ansi(untilUtc.AddTicks(-1).ToPersianDateTimeString("yyyy/MM/dd", false), 10),
                FromUtc = fromUtc, UntilUtc = untilUtc,
                BillId = Ansi(query.Field == MessageSearchField.BillId && query.Value.Length > 0 ? query.Value : null, 31),
                PayId = Ansi(query.Field == MessageSearchField.PayId && query.Value.Length > 0 ? query.Value : null, 31),
                MobileNumber = Ansi(query.Field == MessageSearchField.MobileNumber && query.Value.Length > 0 ? query.Value : null, 15),
                BeforeDate = Ansi(query.Before?.SubmitDateJalali, 10),
                BeforeId = query.Before?.Id,
                Take = PageSize + 1,
            }, cancellationToken: token))).AsList();

        bool hasMore = rows.Count > PageSize;
        if (hasMore) rows.RemoveAt(PageSize);
        for (int index = 0; index < rows.Count; index++)
            rows[index] = rows[index] with { SubmittedAtUtc = DateTime.SpecifyKind(rows[index].SubmittedAtUtc, DateTimeKind.Utc) };
        MessageSearchCursor? next = hasMore ? new(rows[^1].SubmitDateJalali, rows[^1].Id) : null;
        return new MessageSearchPage(rows, next);
    }

    private static DbString Ansi(string? value, int length) => new() { Value = value, IsAnsi = true, Length = length };
}
