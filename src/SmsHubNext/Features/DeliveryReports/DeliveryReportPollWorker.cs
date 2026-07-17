using SmsHubNext.Shared.Health;

namespace SmsHubNext.Features.DeliveryReports;

/// <summary>
/// The delivery-report poll host: a thin <see cref="BackgroundService"/> that only schedules work
/// (ARCHITECTURE.md §9). It owns no business logic, SQL, or retry rules — it opens a scope, asks
/// <see cref="DeliveryReportPoller"/> to process one batch, and loops: immediately while there is
/// work, otherwise idling for <see cref="DeliveryReportPollOptions.PollInterval"/>. Unfinished poll
/// rows simply remain in the queue and are reclaimed after a restart.
/// </summary>
public sealed class DeliveryReportPollWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeliveryReportPollOptions _options;
    private readonly BackgroundWorkerHealthMonitor _health;
    private readonly ILogger<DeliveryReportPollWorker> _logger;

    public DeliveryReportPollWorker(
        IServiceScopeFactory scopeFactory,
        DeliveryReportPollOptions options,
        BackgroundWorkerHealthMonitor health,
        ILogger<DeliveryReportPollWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _health = health;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _health.ReportStarted(BackgroundWorkerNames.DeliveryReports);
        _logger.LogInformation("Delivery-report poll worker started (poll interval {Interval}).", _options.PollInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    DeliveryReportPoller poller = scope.ServiceProvider.GetRequiredService<DeliveryReportPoller>();

                    bool didWork = await poller.PollNextBatchAsync(stoppingToken);
                    _health.ReportSucceeded(BackgroundWorkerNames.DeliveryReports);

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
                    _health.ReportFailed(BackgroundWorkerNames.DeliveryReports);
                    _logger.LogError(ex, "Delivery-report poll cycle failed; backing off.");
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
            }
        }
        finally
        {
            _health.ReportStopped(BackgroundWorkerNames.DeliveryReports);
            _logger.LogInformation("Delivery-report poll worker stopping.");
        }
    }
}
