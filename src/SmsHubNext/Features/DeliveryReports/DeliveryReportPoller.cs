using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DeliveryReports;

/// <summary>
/// One unit of delivery-report polling work: claim the due poll rows, ask the provider for their
/// delivery status, and for each terminal outcome project it onto <c>Message.DeliveryStatus</c>,
/// append the immutable <c>DeliveryReport</c>, and dequeue. Rows whose provider status window has
/// lapsed are expired without a provider call; rows still in flight stay queued for a later cycle.
///
/// Like the dispatcher, all reliability lives in SQL (ARCHITECTURE.md §9): claims are atomic and
/// leased, transitions are guarded (idempotent), and a restart resumes from the queue. Provider
/// selection by <c>ProviderId</c> is a later concern; with one provider registered, every claimed
/// row goes through it.
/// </summary>
public sealed class DeliveryReportPoller
{
    private readonly Db _db;
    private readonly ISmsProvider _provider;
    private readonly DeliveryReportPollOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeliveryReportPoller> _logger;

    public DeliveryReportPoller(
        Db db,
        ISmsProvider provider,
        DeliveryReportPollOptions options,
        TimeProvider clock,
        ILogger<DeliveryReportPoller> logger)
    {
        _db = db;
        _provider = provider;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Process at most one batch of due poll rows. Returns <c>true</c> if any were claimed (so the
    /// caller should loop again promptly), <c>false</c> when nothing was due (idle/back off).
    /// </summary>
    public async Task<bool> PollNextBatchAsync(CancellationToken cancellationToken)
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        DateTime nextPollAt = now + _options.RetryDelay;
        DateTime windowStart = now - _options.StatusWindow;

        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        List<PollRow> rows = (await connection.QueryAsync<PollRow>(new CommandDefinition(
            DeliveryReportsSql.ClaimDuePolls,
            new { _options.BatchSize, Now = now, NextPollAtUtc = nextPollAt },
            cancellationToken: cancellationToken))).AsList();

        if (rows.Count == 0)
            return false;

        _logger.LogDebug("Polling delivery reports for {Count} message(s).", rows.Count);

        // Messages past the provider's status window will never resolve: expire them (terminal)
        // without a provider call. The rest are live and queried below.
        List<PollRow> live = new List<PollRow>(rows.Count);
        foreach (PollRow row in rows)
        {
            if (row.DispatchedAtUtc <= windowStart)
                await ApplyTerminalAsync(connection, row, DeliveryReportStatus.Expired, rawStatusCode: 0, now, cancellationToken);
            else
                live.Add(row);
        }

        if (live.Count == 0)
            return true;

        List<string> providerMessageIds = live.Select(r => r.ProviderMessageId).ToList();
        Result<IReadOnlyList<ProviderDeliveryReport>> query = await SafeQueryAsync(providerMessageIds, cancellationToken);

        if (query.IsFailure)
        {
            // Transient: the rows are already leased forward, so a later cycle retries them.
            _logger.LogWarning("Delivery-report query failed: {Error}. Retrying next cycle.", query.Error!.Message);
            return true;
        }

        Dictionary<string, PollRow> byProviderMessageId = live.ToDictionary(r => r.ProviderMessageId);
        foreach (ProviderDeliveryReport report in query.Value)
        {
            if (report.Status is null)
                continue; // still in flight; remains queued for the next cycle

            if (byProviderMessageId.TryGetValue(report.ProviderMessageId, out PollRow? row))
                await ApplyTerminalAsync(connection, row, report.Status.Value, report.RawStatusCode, now, cancellationToken);
        }

        return true;
    }

    private static async Task ApplyTerminalAsync(
        SqlConnection connection,
        PollRow row,
        DeliveryReportStatus status,
        int rawStatusCode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        DeliveryStatus readModel = status.ToDeliveryStatus();

        using SqlTransaction transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(new CommandDefinition(
            DeliveryReportsSql.ApplyTerminalReport,
            new
            {
                row.MessageId,
                row.SubmitDateJalali,
                DeliveryStatus = (byte)readModel,
                DeliveredValue = (byte)DeliveryStatus.Delivered,
                PendingValue = (byte)DeliveryStatus.Pending,
                NormalizedStatus = (byte)status,
                RawStatusCode = rawStatusCode,
                ReceivedAtUtc = now,
            },
            transaction,
            cancellationToken: cancellationToken));
        transaction.Commit();
    }

    private async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> SafeQueryAsync(
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        try
        {
            return await _provider.GetDeliveryReportsAsync(providerMessageIds, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider should not throw for expected failures; treat anything that escapes as transient.
            _logger.LogError(ex, "Provider threw fetching delivery reports");
            return Error.Provider("dlr.provider_threw", ex.Message);
        }
    }

    private sealed record PollRow(
        long MessageId,
        string SubmitDateJalali,
        byte ProviderId,
        string ProviderMessageId,
        DateTime DispatchedAtUtc,
        int Attempts);
}
