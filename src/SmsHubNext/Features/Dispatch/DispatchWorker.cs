using SmsHubNext.Shared.Health;

namespace SmsHubNext.Features.Dispatch;

/// <summary>
/// The dispatch host: a deliberately thin <see cref="BackgroundService"/> that only
/// schedules work (ARCHITECTURE.md §9). It owns no business logic, SQL, or retry rules —
/// it opens a scope, asks <see cref="MessageDispatcher"/> to process one batch, and loops:
/// immediately while there is work, otherwise idling for <see cref="DispatchOptions.PollInterval"/>.
/// A claimed batch left mid-flight by a restart is simply reclaimed from the database.
/// </summary>
public sealed class DispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DispatchOptions _options;
    private readonly BackgroundWorkerHealthMonitor _health;
    private readonly ILogger<DispatchWorker> _logger;

    public DispatchWorker(
        IServiceScopeFactory scopeFactory,
        DispatchOptions options,
        BackgroundWorkerHealthMonitor health,
        ILogger<DispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _health = health;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _health.ReportStarted(BackgroundWorkerNames.Dispatch);
        _logger.LogInformation("Dispatch worker started (poll interval {Interval}).", _options.PollInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    MessageDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<MessageDispatcher>();

                    bool didWork = await dispatcher.DispatchNextBatchAsync(stoppingToken);
                    _health.ReportSucceeded(BackgroundWorkerNames.Dispatch);

                    if (!didWork)
                        await Task.Delay(_options.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // graceful shutdown
                }
                catch (Exception ex)
                {
                    // Never let a transient failure kill the loop; back off and try again.
                    _health.ReportFailed(BackgroundWorkerNames.Dispatch);
                    _logger.LogError(ex, "Dispatch cycle failed; backing off.");
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
            }
        }
        finally
        {
            _health.ReportStopped(BackgroundWorkerNames.Dispatch);
            _logger.LogInformation("Dispatch worker stopping.");
        }
    }
}
