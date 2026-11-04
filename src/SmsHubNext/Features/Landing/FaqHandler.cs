using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Landing;

public sealed record FaqItem(int Id, string Question, string Answer, int SortOrder);

public sealed class SaveFaqRequest
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;

    public Result Validate() => string.IsNullOrWhiteSpace(Question) || Question.Trim().Length > 500 ||
        string.IsNullOrWhiteSpace(Answer) || Answer.Trim().Length > 4000
        ? Error.Validation("Faq.Invalid", "سؤال (حداکثر ۵۰۰ نویسه) و پاسخ (حداکثر ۴۰۰۰ نویسه) الزامی است.")
        : Result.Success();
}

public sealed class FaqHandler(Db db)
{
    public async Task<Result<IReadOnlyList<FaqItem>>> List(CancellationToken token)
    {
        await using SqlConnection connection = db.CreateConnection();
        IEnumerable<FaqItem> items = await connection.QueryAsync<FaqItem>(new CommandDefinition(
            FaqSql.List, cancellationToken: token));
        return Result.Success<IReadOnlyList<FaqItem>>(items.ToList());
    }

    public async Task<Result> Save(int? id, SaveFaqRequest request, int actorId, CancellationToken token)
    {
        Result validation = request.Validate();
        if (validation.IsFailure) return validation;
        await using SqlConnection connection = db.CreateConnection();
        int changed = await connection.ExecuteAsync(new CommandDefinition(id.HasValue ? FaqSql.Update : FaqSql.Insert,
            new { Id = id, Question = request.Question.Trim(), Answer = request.Answer.Trim(), ActorId = actorId }, cancellationToken: token));
        return changed == 0 ? Missing() : Result.Success();
    }

    public async Task<Result> Delete(int id, CancellationToken token)
    {
        await using SqlConnection connection = db.CreateConnection();
        int changed = await connection.ExecuteAsync(new CommandDefinition(FaqSql.Delete, new { Id = id }, cancellationToken: token));
        return changed == 0 ? Missing() : Result.Success();
    }

    public async Task<Result> Move(int id, int direction, int actorId, CancellationToken token)
    {
        if (direction is not (-1 or 1)) return Error.Validation("Faq.Direction", "جهت جابه‌جایی معتبر نیست.");
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        // Lock the small ordered set so concurrent moves/inserts cannot lose an ordering change.
        List<FaqItem> items = (await connection.QueryAsync<FaqItem>(new CommandDefinition(
            FaqSql.LockedList, transaction: transaction, cancellationToken: token))).ToList();
        int index = items.FindIndex(item => item.Id == id);
        if (index < 0) return Missing();
        int destination = index + direction;
        if (destination < 0 || destination >= items.Count) return Result.Success();
        (items[index], items[destination]) = (items[destination], items[index]);
        for (int position = 0; position < items.Count; position++)
            await connection.ExecuteAsync(new CommandDefinition(FaqSql.Reorder,
                new { items[position].Id, SortOrder = position + 1, ActorId = actorId }, transaction, cancellationToken: token));
        await transaction.CommitAsync(token);
        return Result.Success();
    }

    private static Error Missing() => Error.NotFound("Faq.NotFound", "سؤال پیدا نشد؛ فهرست را تازه‌سازی کنید.");
}
