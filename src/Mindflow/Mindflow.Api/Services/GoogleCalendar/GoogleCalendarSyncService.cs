using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.GoogleCalendar;

public class GoogleCalendarSyncService(
    IGoogleCalendarConnectionRepository connectionRepository,
    ICalendarBlockRepository calendarBlockRepository,
    IGoogleCalendarClient client,
    ITasksNotifier notifier,
    IOptions<GoogleCalendarOptions> options,
    ILogger<GoogleCalendarSyncService> logger) : IGoogleCalendarSyncService
{
    private readonly GoogleCalendarOptions _options = options.Value;

    public async Task<int> SyncUserAsync(Guid userId, CancellationToken ct = default)
    {
        var connection = await connectionRepository.GetByUserIdAsync(userId);
        if (connection is null) return 0;

        GoogleSyncResult result;
        try
        {
            result = await client.ListChangesAsync(connection, connection.SyncToken, ct);
        }
        catch (GoogleSyncTokenExpiredException)
        {
            logger.LogInformation("Google sync token expired for user {UserId}; running a full resync.", userId);
            result = await client.ListChangesAsync(connection, null, ct);
        }

        var applied = 0;
        foreach (var change in result.Changes)
        {
            applied += await ApplyChangeAsync(userId, change);
        }

        connection.SyncToken = result.NewSyncToken;
        connection.LastSyncedAt = DateTimeOffset.UtcNow;
        connection.RequiresReconnect = false;
        connection.UpdatedAt = connection.LastSyncedAt.Value;
        await connectionRepository.UpdateAsync(connection);

        return applied;
    }

    private async Task<int> ApplyChangeAsync(Guid userId, GoogleEventChange change)
    {
        var existing = await calendarBlockRepository.GetByExternalEventIdAsync(userId, change.EventId);

        if (change.IsDeleted)
        {
            if (existing is null) return 0;
            await calendarBlockRepository.DeleteAsync(existing);
            await SafeNotifyAsync(() => notifier.CalendarBlockDeletedAsync(existing.Id, userId), "mirror-delete");
            return 1;
        }

        if (existing is null)
        {
            var now = DateTimeOffset.UtcNow;
            var block = new CalendarBlock
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TaskId = null,
                Title = change.Title,
                StartAt = change.Start.ToUniversalTime(),
                DurationMinutes = change.DurationMinutes,
                CreatedAt = now,
                UpdatedAt = now,
                Provider = CalendarBlockProvider.Google,
                ExternalEventId = change.EventId,
                GoogleCalendarId = null,
                SyncStatus = CalendarBlockSyncStatus.Synced
            };
            var created = await calendarBlockRepository.CreateAsync(block);
            await SafeNotifyAsync(() => notifier.CalendarBlockCreatedAsync(CalendarBlockMapper.ToResponse(created)), "mirror-create");
            return 1;
        }

        existing.Title = change.Title;
        existing.StartAt = change.Start.ToUniversalTime();
        existing.DurationMinutes = change.DurationMinutes;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        var updated = await calendarBlockRepository.UpdateAsync(existing);
        await SafeNotifyAsync(() => notifier.CalendarBlockUpdatedAsync(CalendarBlockMapper.ToResponse(updated)), "mirror-update");
        return 1;
    }

    public async Task PushBlockCreatedAsync(CalendarBlock block, CancellationToken ct = default)
    {
        if (block.Provider != CalendarBlockProvider.Local) return;
        var connection = await connectionRepository.GetByUserIdAsync(block.UserId);
        if (connection is null) return;

        try
        {
            block.GoogleCalendarId = connection.DedicatedCalendarId;
            var eventId = await client.UpsertEventAsync(connection, block, ct);
            block.ExternalEventId = eventId;
            block.SyncStatus = CalendarBlockSyncStatus.Synced;
            block.UpdatedAt = DateTimeOffset.UtcNow;
            var updated = await calendarBlockRepository.UpdateAsync(block);
            await SafeNotifyAsync(() => notifier.CalendarBlockUpdatedAsync(CalendarBlockMapper.ToResponse(updated)), "push-create");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push created block {BlockId} to Google.", block.Id);
        }
    }

    public async Task PushBlockUpdatedAsync(CalendarBlock block, CancellationToken ct = default)
    {
        if (block.Provider != CalendarBlockProvider.Local) return;
        var connection = await connectionRepository.GetByUserIdAsync(block.UserId);
        if (connection is null) return;

        try
        {
            block.GoogleCalendarId ??= connection.DedicatedCalendarId;
            var eventId = await client.UpsertEventAsync(connection, block, ct);
            if (block.ExternalEventId != eventId || block.SyncStatus != CalendarBlockSyncStatus.Synced)
            {
                block.ExternalEventId = eventId;
                block.SyncStatus = CalendarBlockSyncStatus.Synced;
                block.UpdatedAt = DateTimeOffset.UtcNow;
                await calendarBlockRepository.UpdateAsync(block);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push updated block {BlockId} to Google.", block.Id);
        }
    }

    public async Task PushBlockDeletedAsync(CalendarBlock block, CancellationToken ct = default)
    {
        if (block.Provider != CalendarBlockProvider.Local) return;
        if (string.IsNullOrWhiteSpace(block.ExternalEventId) || string.IsNullOrWhiteSpace(block.GoogleCalendarId)) return;

        var connection = await connectionRepository.GetByUserIdAsync(block.UserId);
        if (connection is null) return;

        try
        {
            await client.DeleteEventAsync(connection, block.GoogleCalendarId, block.ExternalEventId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete Google event for block {BlockId}.", block.Id);
        }
    }

    public async Task<int> RetryPendingLocalBlocksAsync(Guid userId, CancellationToken ct = default)
    {
        var connection = await connectionRepository.GetByUserIdAsync(userId);
        if (connection is null) return 0;

        var pending = await calendarBlockRepository.GetPendingGooglePushAsync(userId);
        var pushed = 0;

        foreach (var block in pending)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                block.GoogleCalendarId = connection.DedicatedCalendarId;
                var eventId = await client.UpsertEventAsync(connection, block, ct);
                block.ExternalEventId = eventId;
                block.SyncStatus = CalendarBlockSyncStatus.Synced;
                block.UpdatedAt = DateTimeOffset.UtcNow;
                var updated = await calendarBlockRepository.UpdateAsync(block);
                await SafeNotifyAsync(
                    () => notifier.CalendarBlockUpdatedAsync(CalendarBlockMapper.ToResponse(updated)),
                    "push-retry");
                pushed++;
            }
            catch (GoogleCalendarReconnectRequiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to retry Google push for block {BlockId}.", block.Id);
            }
        }

        return pushed;
    }

    public async Task EnsureWatchAsync(GoogleCalendarConnection connection, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            logger.LogInformation("Google:Calendar:WebhookUrl not set — skipping push watch (manual/poll sync only).");
            return;
        }

        // stop any previous channel so we don't accumulate duplicates
        if (!string.IsNullOrWhiteSpace(connection.WatchChannelId) && !string.IsNullOrWhiteSpace(connection.WatchResourceId))
        {
            try { await client.StopWatchAsync(connection, connection.WatchChannelId, connection.WatchResourceId, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to stop previous Google watch channel."); }
        }

        var channelId = Guid.NewGuid().ToString("N");
        var channelToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var watch = await client.StartWatchAsync(connection, channelId, channelToken, _options.WebhookUrl, ct);

        connection.WatchChannelId = channelId;
        connection.WatchToken = channelToken;
        connection.WatchResourceId = watch.ResourceId;
        connection.WatchExpiresAt = watch.ExpiresAt;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await connectionRepository.UpdateAsync(connection);
    }

    private async Task SafeNotifyAsync(Func<Task> publish, string label)
    {
        try { await publish(); }
        catch (Exception ex) { logger.LogWarning(ex, "SignalR publish failed during {Label}.", label); }
    }
}
