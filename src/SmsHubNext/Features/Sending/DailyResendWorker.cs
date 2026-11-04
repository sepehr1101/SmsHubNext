namespace SmsHubNext.Features.Sending;

public sealed class DailyResendWorker(IServiceScopeFactory scopes, ILogger<DailyResendWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                bool worked = await scope.ServiceProvider.GetRequiredService<DailyResendProcessor>().ProcessNext(stoppingToken);
                if (!worked) await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Daily resend processing failed; durable manifest will be resumed");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
