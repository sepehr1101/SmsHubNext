namespace SmsHubNext.Features.Inbound;

/// <summary>
/// The inbound-poll host: a thin <see cref="BackgroundService"/> that only schedules work
/// (ARCHITECTURE.md §9). It opens a scope, asks <see cref="InboundPoller"/> to pull and persist one
/// page, and loops: immediately while full pages keep coming, otherwise idling for
/// <see cref="InboundPollOptions.PollInterval"/>. Only hosted when inbound polling is enabled.
/// </summary>
public sealed class InboundPollWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InboundPollOptions _options;
    private readonly ILogger<InboundPollWorker> _logger;

    public InboundPollWorker(
        IServiceScopeFactory scopeFactory,
        InboundPollOptions options,
        ILogger<InboundPollWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inbound poll worker started (poll interval {Interval}).", _options.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                InboundPoller poller = scope.ServiceProvider.GetRequiredService<InboundPoller>();

                bool morePending = await poller.PollOnceAsync(stoppingToken);

                if (!morePending)
                    await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                // Never let a transient failure kill the loop; back off and try again.
                _logger.LogError(ex, "Inbound poll cycle failed; backing off.");
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Inbound poll worker stopping.");
    }
}
