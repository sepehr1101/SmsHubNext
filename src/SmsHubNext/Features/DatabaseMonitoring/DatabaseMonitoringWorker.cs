namespace SmsHubNext.Features.DatabaseMonitoring;

public sealed class DatabaseMonitoringWorker(IServiceScopeFactory scopes, ILogger<DatabaseMonitoringWorker> logger, TimeProvider clock, DatabaseMonitoringCollectorState state) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DatabaseMonitoringHandler>().Collect(stoppingToken);
                state.Succeeded(clock.GetUtcNow().UtcDateTime);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { state.Failed(clock.GetUtcNow().UtcDateTime); logger.LogWarning(exception, "Database monitoring collection failed; the next attempt is in 15 minutes"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

