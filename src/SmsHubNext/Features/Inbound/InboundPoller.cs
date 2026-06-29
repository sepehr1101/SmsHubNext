using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Inbound;

/// <summary>
/// One unit of inbound-poll work: pull a page of MO messages from the provider and persist them.
/// The provider pull is destructive (fetched messages are dequeued there), so each page is written
/// immediately, in one bulk insert. There is no provider-side message id to dedupe on, so this is
/// at-most-once — a crash between pull and persist loses that page (an inherent property of the
/// provider's destructive inbox, not a bug here).
/// </summary>
public sealed class InboundPoller
{
    private readonly Db _db;
    private readonly ISmsProvider _provider;
    private readonly InboundPollOptions _options;
    private readonly ILogger<InboundPoller> _logger;

    public InboundPoller(Db db, ISmsProvider provider, InboundPollOptions options, ILogger<InboundPoller> logger)
    {
        _db = db;
        _provider = provider;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Pull and persist one page. Returns <c>true</c> if a full page was ingested (more may be
    /// waiting, so poll again promptly), <c>false</c> when the inbox was empty or the pull failed.
    /// </summary>
    public async Task<bool> PollOnceAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ProviderInboundMessage>> fetch = await SafeFetchAsync(cancellationToken);
        if (fetch.IsFailure)
        {
            _logger.LogWarning("Inbound fetch failed: {Error}. Retrying next cycle.", fetch.Error!.Message);
            return false;
        }

        IReadOnlyList<ProviderInboundMessage> inbound = fetch.Value;
        if (inbound.Count == 0)
            return false;

        await PersistAsync(inbound, cancellationToken);
        _logger.LogDebug("Ingested {Count} inbound message(s).", inbound.Count);

        // A full page likely means more are waiting; ask the caller to poll again immediately.
        return inbound.Count >= _options.BatchSize;
    }

    private async Task PersistAsync(IReadOnlyList<ProviderInboundMessage> inbound, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        byte providerId = await connection.ExecuteScalarAsync<byte>(new CommandDefinition(
            InboundSql.ResolveProviderId, new { Code = _provider.Name }, cancellationToken: cancellationToken));

        using SqlTransaction transaction = connection.BeginTransaction();
        using DataTable rows = BuildRows(inbound, providerId);
        await connection.BulkInsertAsync(transaction, "dbo.InboundMessage", rows, cancellationToken);
        transaction.Commit();
    }

    private static DataTable BuildRows(IReadOnlyList<ProviderInboundMessage> inbound, byte providerId)
    {
        DataTable table = new DataTable();
        table.Columns.Add("ProviderId", typeof(byte));
        table.Columns.Add("SenderNumber", typeof(string));
        table.Columns.Add("RecipientNumber", typeof(string));
        table.Columns.Add("Body", typeof(string));
        table.Columns.Add("ProviderTimestamp", typeof(string));

        foreach (ProviderInboundMessage message in inbound)
        {
            table.Rows.Add(
                providerId,
                message.SenderNumber,
                message.RecipientNumber,
                message.Body,
                (object?)message.ProviderTimestamp ?? DBNull.Value);
        }

        return table;
    }

    private async Task<Result<IReadOnlyList<ProviderInboundMessage>>> SafeFetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _provider.FetchInboundMessagesAsync(_options.BatchSize, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider should not throw for expected failures; treat anything that escapes as transient.
            _logger.LogError(ex, "Provider threw fetching inbound messages");
            return Error.Provider("inbound.provider_threw", ex.Message);
        }
    }
}
