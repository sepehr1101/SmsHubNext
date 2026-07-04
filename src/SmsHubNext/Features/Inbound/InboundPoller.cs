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
    private readonly SmsProviderRegistry _providers;
    private readonly InboundPollOptions _options;
    private readonly ILogger<InboundPoller> _logger;

    public InboundPoller(Db db, SmsProviderRegistry providers, InboundPollOptions options, ILogger<InboundPoller> logger)
    {
        _db = db;
        _providers = providers;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Pull and persist one page. Returns <c>true</c> if a full page was ingested (more may be
    /// waiting, so poll again promptly), <c>false</c> when the inbox was empty or the pull failed.
    /// </summary>
    public async Task<bool> PollOnceAsync(CancellationToken cancellationToken)
    {
        bool moreLikely = false;
        foreach (ISmsProvider provider in _providers.Providers)
        {
            Result<IReadOnlyList<ProviderInboundMessage>> fetch = await SafeFetchAsync(provider, cancellationToken);
            if (fetch.IsFailure)
            {
                _logger.LogWarning(
                    "Inbound fetch failed for provider {Provider}: {Error}. Retrying next cycle.",
                    provider.Name,
                    fetch.Error!.Message);
                continue;
            }

            IReadOnlyList<ProviderInboundMessage> inbound = fetch.Value;
            if (inbound.Count == 0)
                continue;

            await PersistAsync(provider, inbound, cancellationToken);
            _logger.LogDebug(
                "Ingested {Count} inbound message(s) from provider {Provider}.",
                inbound.Count,
                provider.Name);

            moreLikely = moreLikely || inbound.Count >= _options.BatchSize;
        }

        return moreLikely;
    }

    private async Task PersistAsync(
        ISmsProvider provider,
        IReadOnlyList<ProviderInboundMessage> inbound,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);

        byte? providerId = await connection.ExecuteScalarAsync<byte?>(new CommandDefinition(
            InboundSql.ResolveProviderId, new { Code = provider.Name }, cancellationToken: cancellationToken));

        if (providerId is null)
        {
            _logger.LogWarning("Inbound provider {Provider} is registered in code but not present in reference data.", provider.Name);
            return;
        }

        using SqlTransaction transaction = connection.BeginTransaction();
        using DataTable rows = BuildRows(inbound, providerId.Value);
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

    private async Task<Result<IReadOnlyList<ProviderInboundMessage>>> SafeFetchAsync(
        ISmsProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.FetchInboundMessagesAsync(_options.BatchSize, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider should not throw for expected failures; treat anything that escapes as transient.
            _logger.LogError(ex, "Provider {Provider} threw fetching inbound messages", provider.Name);
            return Error.Provider("inbound.provider_threw", UserMessages.Providers.ProviderThrew);
        }
    }
}
