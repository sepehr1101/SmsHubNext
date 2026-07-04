using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;

namespace SmsHubNext.Features.Tariffs;

/// <summary>
/// Prices a message: counts segments (<see cref="SmsPartCalculator"/>), resolves the
/// active tariff rate, and multiplies. The same resolution feeds the cost snapshot
/// frozen onto a <c>Message</c> at submission.
/// </summary>
public sealed class QuoteHandler
{
    private readonly Db _db;

    public QuoteHandler(Db db) => _db = db;

    public async Task<Result<CostQuote>> Handle(QuoteRequest request, CancellationToken cancellationToken)
    {
        Result validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        SmsSegmentInfo segments = SmsPartCalculator.Calculate(request.Text);

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        CostRow? row = await connection.QuerySingleOrDefaultAsync<CostRow>(new CommandDefinition(
            TariffsSql.ResolveRate,
            new
            {
                request.ProviderId,
                request.MessageTypeId,
                Encoding = (byte)segments.Encoding,
                segments.CharacterCount,
            },
            cancellationToken: cancellationToken));

        if (row is null)
            return Error.NotFound("tariffs.no_rate", UserMessages.Tariffs.NoRate);

        decimal totalCost = segments.SegmentCount * row.PricePerSegment;

        return new CostQuote(
            segments.Encoding,
            segments.CharacterCount,
            segments.SegmentCount,
            row.TariffId,
            row.PricePerSegment,
            totalCost,
            row.Currency);
    }
}

/// <summary>The row shape returned by the tariff-resolution query.</summary>
internal sealed record CostRow(int TariffId, string Currency, decimal PricePerSegment);
