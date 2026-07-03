using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DeliveryReports;

/// <summary>
/// One unit of delivery-report polling work: claim the due poll rows, ask the provider for their
/// delivery status, and apply each terminal outcome — project it onto <c>Message.DeliveryStatus</c>,
/// append the immutable <c>DeliveryReport</c>, and dequeue. Rows whose provider status window has
/// lapsed are expired without a provider call; rows still in flight stay queued for a later cycle.
///
/// A cycle's terminal outcomes are applied together: bulk-loaded (<c>SqlBulkCopy</c>) into a temp
/// table and merged set-based in a single transaction, so the whole apply is atomic (ACID) and the
/// hot path is one round trip instead of one per message. Like the dispatcher, all reliability lives
/// in SQL (ARCHITECTURE.md §9): claims are atomic and leased, transitions are guarded (idempotent),
/// and a restart resumes from the queue. Provider selection by <c>ProviderId</c> is a later concern;
/// with one provider registered, every claimed row goes through it.
/// </summary>
public sealed class DeliveryReportPoller
{
    private readonly Db _db;
    private readonly SmsProviderRegistry _providers;
    private readonly DeliveryReportPollOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeliveryReportPoller> _logger;

    public DeliveryReportPoller(
        Db db,
        SmsProviderRegistry providers,
        DeliveryReportPollOptions options,
        TimeProvider clock,
        ILogger<DeliveryReportPoller> logger)
    {
        _db = db;
        _providers = providers;
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

        // Accumulate this cycle's terminal outcomes; they are applied together in one bulk
        // transaction below. A HashSet guards against the same message appearing twice (a
        // misbehaving provider could repeat a mid) so the temp table's PK can't be violated.
        List<TerminalApply> applies = new List<TerminalApply>(rows.Count);
        HashSet<long> seen = new HashSet<long>(rows.Count);
        List<PollRow> live = new List<PollRow>(rows.Count);

        foreach (PollRow row in rows)
        {
            // Past the provider's status window it will never resolve: expire it (terminal) with no call.
            if (row.DispatchedAtUtc <= windowStart)
            {
                if (seen.Add(row.MessageId))
                    applies.Add(TerminalApply.For(row, DeliveryReportStatus.Expired, rawStatusCode: 0, now));
            }
            else
            {
                live.Add(row);
            }
        }

        if (live.Count > 0)
        {
            foreach (IGrouping<byte, PollRow> providerGroup in live.GroupBy(row => row.ProviderId))
            {
                string providerCode = await connection.QuerySingleAsync<string>(new CommandDefinition(
                    DeliveryReportsSql.GetProviderCode,
                    new { ProviderId = providerGroup.Key },
                    cancellationToken: cancellationToken));

                Result<ISmsProvider> provider = _providers.Resolve(providerCode);
                if (provider.IsFailure)
                {
                    _logger.LogWarning("Delivery-report provider is not registered: {Error}. Retrying next cycle.", provider.Error!.Message);
                    continue;
                }

                List<PollRow> providerRows = providerGroup.ToList();
                List<string> providerMessageIds = providerRows.Select(r => r.ProviderMessageId).ToList();
                Result<IReadOnlyList<ProviderDeliveryReport>> query = await SafeQueryAsync(
                    provider.Value, providerMessageIds, cancellationToken);

                if (query.IsFailure)
                {
                    // Transient: the live rows are leased forward and retried next cycle. Any expiries
                    // gathered above are independent of the provider and are still applied below.
                    _logger.LogWarning("Delivery-report query failed: {Error}. Retrying next cycle.", query.Error!.Message);
                    continue;
                }

                Dictionary<string, PollRow> byProviderMessageId = providerRows.ToDictionary(r => r.ProviderMessageId);
                foreach (ProviderDeliveryReport report in query.Value)
                {
                    if (report.Status is null)
                        continue; // still in flight; remains queued for the next cycle

                    if (byProviderMessageId.TryGetValue(report.ProviderMessageId, out PollRow? row) && seen.Add(row.MessageId))
                        applies.Add(TerminalApply.For(row, report.Status.Value, report.RawStatusCode, now));
                }
            }
        }

        if (applies.Count > 0)
            await ApplyTerminalsAsync(connection, applies, cancellationToken);

        return true;
    }

    // Apply a whole cycle's terminal outcomes atomically: bulk-load them into a temp table, then run
    // the guarded set-based projection/append/dequeue in one transaction.
    private static async Task ApplyTerminalsAsync(
        SqlConnection connection,
        IReadOnlyList<TerminalApply> applies,
        CancellationToken cancellationToken)
    {
        using SqlTransaction transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            DeliveryReportsSql.CreateTerminalApplyTemp,
            transaction: transaction,
            cancellationToken: cancellationToken));

        using DataTable rows = BuildTerminalApplyRows(applies);
        await connection.BulkInsertAsync(transaction, "#TerminalApply", rows, cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            DeliveryReportsSql.ApplyTerminalReports,
            new
            {
                DeliveredValue = (byte)DeliveryStatus.Delivered,
                PendingValue = (byte)DeliveryStatus.Pending,
                DeliveryUpdatedEventType = (byte)MessageBatchEventType.DeliveryUpdated,
            },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
    }

    private static DataTable BuildTerminalApplyRows(IReadOnlyList<TerminalApply> applies)
    {
        DataTable table = new DataTable();
        table.Columns.Add("MessageId", typeof(long));
        table.Columns.Add("SubmitDateJalali", typeof(string));
        table.Columns.Add("DeliveryStatus", typeof(byte));
        table.Columns.Add("NormalizedStatus", typeof(byte));
        table.Columns.Add("RawStatusCode", typeof(int));
        table.Columns.Add("ReceivedAtUtc", typeof(DateTime));

        foreach (TerminalApply apply in applies)
        {
            table.Rows.Add(
                apply.MessageId,
                apply.SubmitDateJalali,
                apply.DeliveryStatus,
                apply.NormalizedStatus,
                apply.RawStatusCode,
                apply.ReceivedAtUtc);
        }

        return table;
    }

    private async Task<Result<IReadOnlyList<ProviderDeliveryReport>>> SafeQueryAsync(
        ISmsProvider provider,
        IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetDeliveryReportsAsync(providerMessageIds, cancellationToken);
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

    /// <summary>A resolved terminal outcome ready to stage: the read-model projection and the
    /// normalized history status, derived once from the provider's <see cref="DeliveryReportStatus"/>.</summary>
    private sealed record TerminalApply(
        long MessageId,
        string SubmitDateJalali,
        byte DeliveryStatus,
        byte NormalizedStatus,
        int RawStatusCode,
        DateTime ReceivedAtUtc)
    {
        public static TerminalApply For(PollRow row, DeliveryReportStatus status, int rawStatusCode, DateTime receivedAtUtc) =>
            new(row.MessageId, row.SubmitDateJalali, (byte)status.ToDeliveryStatus(), (byte)status, rawStatusCode, receivedAtUtc);
    }
}
