namespace Mindflow.Api.Services.Telegram;

public class TelegramPollingService(IServiceScopeFactory scopeFactory, ILogger<TelegramPollingService> logger) : BackgroundService
{
    private const int PollTimeoutSeconds = 25;

    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!await DeleteWebhookAsync(stoppingToken))
                await Task.Delay(ErrorDelay, stoppingToken);

            long offset = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var next = await PollOnceAsync(offset, stoppingToken);
                    if (next is { } value) offset = value;
                    else await Task.Delay(ErrorDelay, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Telegram polling failed with {ExceptionType}", ex.GetType().Name);
                    await Task.Delay(ErrorDelay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task<long?> PollOnceAsync(long offset, CancellationToken ct)
    {
        IReadOnlyList<TelegramUpdate>? updates;
        using (var scope = scopeFactory.CreateScope())
            updates = await scope.ServiceProvider.GetRequiredService<TelegramApi>().GetUpdatesAsync(offset, PollTimeoutSeconds, ct);
        if (updates is null) return null;

        foreach (var update in updates)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ITelegramService>().HandleUpdateAsync(update, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError("Telegram update handling failed with {ExceptionType}", ex.GetType().Name);
            }
        }

        return updates.Count == 0 ? offset : Math.Max(offset, updates.Max(u => u.UpdateId) + 1);
    }

    private async Task<bool> DeleteWebhookAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<TelegramApi>().DeleteWebhookAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Telegram deleteWebhook failed with {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }
}
