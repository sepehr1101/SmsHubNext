using DbUp.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.Inbound;
using SmsHubNext.Features.Providers;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Inbound;

/// <summary>
/// The inbound (MO) pipeline end-to-end: the poller pulls a page from the provider and persists it,
/// and the read API lists it newest-first with an optional recipient filter.
/// </summary>
public sealed class InboundTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();

        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);

        _db = new Db(connectionString);
        await ReferenceDataTestData.EnsureDefaultsAsync(_db);
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Poller_persists_a_fetched_page()
    {
        InboundPoller poller = Poller(() => Result.Success<IReadOnlyList<ProviderInboundMessage>>(
        [
            new ProviderInboundMessage("989120000001", "983000711", "سلام", "2026-06-29 09:00:00"),
            new ProviderInboundMessage("989120000002", "983000711", "hi", null),
        ]));

        bool more = await poller.PollOnceAsync(CancellationToken.None);

        Assert.False(more); // 2 < BatchSize, so nothing more to pull
        List<InboundMessage> stored = await ListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, m => Assert.Equal((byte)1, m.ProviderId)); // resolved from provider code 'magfa'

        InboundMessage withTimestamp = stored.Single(m => m.SenderNumber == "989120000001");
        Assert.Equal("983000711", withTimestamp.RecipientNumber);
        Assert.Equal("سلام", withTimestamp.Body);
        Assert.Equal("2026-06-29 09:00:00", withTimestamp.ProviderTimestamp);

        InboundMessage withoutTimestamp = stored.Single(m => m.SenderNumber == "989120000002");
        Assert.Null(withoutTimestamp.ProviderTimestamp);
    }

    [Fact]
    public async Task Poller_persists_nothing_when_the_inbox_is_empty()
    {
        bool more = await Poller(() => Result.Success<IReadOnlyList<ProviderInboundMessage>>([]))
            .PollOnceAsync(CancellationToken.None);

        Assert.False(more);
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Poller_persists_nothing_when_the_fetch_fails()
    {
        bool more = await Poller(() => Result.Failure<IReadOnlyList<ProviderInboundMessage>>(
                Error.Provider("magfa.timeout", "boom")))
            .PollOnceAsync(CancellationToken.None);

        Assert.False(more);
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task List_filters_by_recipient_and_orders_newest_first()
    {
        await Poller(() => Result.Success<IReadOnlyList<ProviderInboundMessage>>(
            [new ProviderInboundMessage("9891", "983000711", "first", null)]))
            .PollOnceAsync(CancellationToken.None);
        await Poller(() => Result.Success<IReadOnlyList<ProviderInboundMessage>>(
            [new ProviderInboundMessage("9892", "983000999", "second", null)]))
            .PollOnceAsync(CancellationToken.None);

        List<InboundMessage> forOneLine = await ListAsync(recipient: "983000711");
        InboundMessage only = Assert.Single(forOneLine);
        Assert.Equal("9891", only.SenderNumber);

        List<InboundMessage> all = await ListAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("second", all[0].Body); // newest first (ReceivedAtUtc DESC, Id DESC)
    }

    private InboundPoller Poller(Func<Result<IReadOnlyList<ProviderInboundMessage>>> fetch) =>
        new(
            _db,
            new SmsProviderRegistry([new InboundStub(fetch)]),
            new InboundPollOptions { BatchSize = 50 },
            NullLogger<InboundPoller>.Instance);

    private async Task<List<InboundMessage>> ListAsync(string? recipient = null, int take = 100)
    {
        Result<IReadOnlyList<InboundMessage>> result =
            await new ListInboundMessagesHandler(_db).Handle(recipient, take, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value.ToList();
    }

    // Inbound-only stub; its Name matches the test-configured 'magfa' provider so the poller can resolve the FK.
    private sealed class InboundStub : ISmsProvider
    {
        private readonly Func<Result<IReadOnlyList<ProviderInboundMessage>>> _fetch;

        public InboundStub(Func<Result<IReadOnlyList<ProviderInboundMessage>>> fetch) => _fetch = fetch;

        public string Name => "magfa";

        public int MaxBatchSize => 100;

        public bool SupportsIdempotentResend => true;

        public Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
            int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult(_fetch());

        public Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
            IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<string?>> ResolveSubmittedMessageIdAsync(long messageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
            IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
