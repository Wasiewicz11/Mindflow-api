namespace Mindflow.Api.Services;

/// <summary>Backfills missed occurrences after process restarts and while the app is online.</summary>
public class TaskRecurrenceMaterializerHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<TaskRecurrenceMaterializerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var recurrenceService = scope.ServiceProvider.GetRequiredService<ITaskRecurrenceService>();
                var created = await recurrenceService.MaterializePastDueOccurrencesAsync(stoppingToken);
                if (created > 0) logger.LogInformation("Materialized {Count} missed recurring task occurrences.", created);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A deployment can begin before its migration has run; the next interval retries
                // rather than leaving recurrence generation permanently disabled.
                logger.LogError(exception, "Recurring task materialization failed; it will retry.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
