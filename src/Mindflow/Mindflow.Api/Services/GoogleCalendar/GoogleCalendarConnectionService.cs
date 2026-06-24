using Microsoft.Extensions.Options;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.GoogleCalendar;

public class GoogleCalendarConnectionService(
    IGoogleCalendarClient client,
    IGoogleCalendarConnectionRepository connectionRepository,
    ICalendarBlockRepository calendarBlockRepository,
    IGoogleCalendarSyncService syncService,
    IGoogleTokenProtector tokenProtector,
    IOAuthStateProtector stateProtector,
    ICurrentUserService currentUserService,
    ITasksNotifier notifier,
    IOptions<GoogleCalendarOptions> options,
    ILogger<GoogleCalendarConnectionService> logger) : IGoogleCalendarConnectionService
{
    private readonly GoogleCalendarOptions _options = options.Value;

    public async Task<string> BeginConnectAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var state = stateProtector.Create(userId);
        return client.BuildConsentUrl(state);
    }

    public async Task<bool> CompleteConnectAsync(string code, Guid userId, CancellationToken ct = default)
    {
        var tokens = await client.ExchangeCodeAsync(code, ct);
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            // Google only returns a refresh token on the first consent or with prompt=consent.
            logger.LogWarning("Google did not return a refresh token for user {UserId}.", userId);
            return false;
        }

        var previous = await connectionRepository.GetByUserIdAsync(userId);
        var now = DateTimeOffset.UtcNow;

        if (previous is not null
            && string.Equals(previous.GoogleAccountEmail, tokens.Email, StringComparison.OrdinalIgnoreCase))
        {
            // Reconnect the same Google account in place. In particular, keep the existing
            // dedicated Mindflow calendar instead of creating another calendar on every consent.
            previous.GoogleAccountEmail = tokens.Email;
            previous.RefreshTokenEncrypted = tokenProtector.Protect(tokens.RefreshToken);
            previous.AccessTokenEncrypted = tokenProtector.Protect(tokens.AccessToken);
            previous.AccessTokenExpiresAt = tokens.ExpiresAt;
            previous.RequiresReconnect = false;
            previous.UpdatedAt = now;
            await connectionRepository.UpdateAsync(previous);

            await StopWatchSafelyAsync(previous, ct);
            ResetSourceSelection(previous, now);
            await connectionRepository.UpdateAsync(previous);
            return true;
        }

        // A different Google account gets its own dedicated calendar and mirror state.
        if (previous is not null)
            await RemoveConnectionAsync(previous, ct);

        var connection = new GoogleCalendarConnection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GoogleAccountEmail = tokens.Email,
            RefreshTokenEncrypted = tokenProtector.Protect(tokens.RefreshToken),
            AccessTokenEncrypted = tokenProtector.Protect(tokens.AccessToken),
            AccessTokenExpiresAt = tokens.ExpiresAt,
            DedicatedCalendarId = string.Empty,
            SourceCalendarId = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        connection.DedicatedCalendarId =
            await client.CreateDedicatedCalendarAsync(connection, _options.DedicatedCalendarName, ct);

        await connectionRepository.CreateAsync(connection);

        return true;
    }

    public async Task<GoogleCalendarStatusResponse> GetStatusAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var connection = await connectionRepository.GetByUserIdAsync(userId);
        if (connection is null)
            return new GoogleCalendarStatusResponse(false, null, null, false, null, false, null, null);

        var pushEnabled = connection.WatchChannelId is not null
            && connection.WatchExpiresAt > DateTimeOffset.UtcNow
            && !connection.RequiresReconnect;

        return new GoogleCalendarStatusResponse(
            true,
            connection.GoogleAccountEmail,
            connection.CreatedAt,
            pushEnabled,
            connection.SourceCalendarId,
            connection.RequiresReconnect,
            connection.WatchExpiresAt,
            connection.LastSyncedAt);
    }

    public async Task<IReadOnlyList<GoogleCalendarListEntry>> GetCalendarsAsync(CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var connection = await connectionRepository.GetByUserIdAsync(userId);
        if (connection is null) return [];
        return await client.ListCalendarsAsync(connection, ct);
    }

    public async Task SetSourceCalendarAsync(string calendarId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
            throw new BadRequestException("Calendar id is required.");

        var userId = await currentUserService.GetCurrentUserIdAsync();
        var connection = await connectionRepository.GetByUserIdAsync(userId)
            ?? throw new NotFoundException("Google Calendar is not connected.");

        if (string.Equals(connection.SourceCalendarId, calendarId, StringComparison.Ordinal))
            return;

        var calendars = await client.ListCalendarsAsync(connection, ct);
        if (calendars.All(c => c.Id != calendarId))
            throw new BadRequestException("Selected calendar is not available on this Google account.");

        // 1) drop the mirror blocks that came from the previous calendar
        var mirrored = await calendarBlockRepository.GetByProviderAsync(userId, CalendarBlockProvider.Google);
        foreach (var block in mirrored)
        {
            await calendarBlockRepository.DeleteAsync(block);
            try { await notifier.CalendarBlockDeletedAsync(block.Id, userId); }
            catch (Exception ex) { logger.LogWarning(ex, "SignalR publish failed while switching source calendar."); }
        }

        // 2) point at the new calendar and reset the per-calendar sync token
        connection.SourceCalendarId = calendarId;
        connection.SyncToken = null;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await connectionRepository.UpdateAsync(connection);

        // 3) move the push channel to the new calendar, then pull a fresh full sync
        await syncService.EnsureWatchAsync(connection, ct);
        await syncService.SyncUserAsync(userId, ct);
        await syncService.RetryPendingLocalBlocksAsync(userId, ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var connection = await connectionRepository.GetByUserIdAsync(userId);
        if (connection is null) return;
        await RemoveConnectionAsync(connection, ct);
    }

    public async Task<GoogleCalendarSyncResponse> SyncCurrentUserAsync(CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var connection = await connectionRepository.GetByUserIdAsync(userId);
        if (connection is null)
            return new GoogleCalendarSyncResponse(0, 0);

        if (connection.RequiresReconnect)
            throw new GoogleCalendarReconnectRequiredException();

        // OAuth succeeded, but the user still has to explicitly choose which Google
        // calendar should be mirrored. Never silently fall back to "primary" here.
        if (string.IsNullOrWhiteSpace(connection.SourceCalendarId))
            return new GoogleCalendarSyncResponse(0, 0);

        var changes = await syncService.SyncUserAsync(userId, ct);

        if (connection.WatchChannelId is null
            || connection.WatchExpiresAt <= DateTimeOffset.UtcNow.AddDays(2))
        {
            await syncService.EnsureWatchAsync(connection, ct);
        }

        var pushed = await syncService.RetryPendingLocalBlocksAsync(userId, ct);
        return new GoogleCalendarSyncResponse(changes, pushed);
    }

    public async Task HandleWebhookAsync(string? channelId, string? token, string? resourceState, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return;

        var connection = await connectionRepository.GetByWatchChannelIdAsync(channelId);
        if (connection is null) return;

        if (!string.Equals(connection.WatchToken, token, StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected Google webhook with mismatched token for channel {ChannelId}.", channelId);
            return;
        }

        // the very first "sync" ping just confirms the channel; real changes come as "exists"
        if (string.Equals(resourceState, "sync", StringComparison.OrdinalIgnoreCase)) return;

        await syncService.SyncUserAsync(connection.UserId, ct);
    }

    public async Task RenewExpiringWatchesAsync(CancellationToken ct = default)
    {
        var threshold = DateTimeOffset.UtcNow.AddDays(2);
        var connections = await connectionRepository.GetWatchesExpiringBeforeAsync(threshold);
        foreach (var connection in connections)
        {
            if (string.IsNullOrWhiteSpace(connection.SourceCalendarId))
                continue;

            try
            {
                await syncService.SyncUserAsync(connection.UserId, ct);
                await syncService.EnsureWatchAsync(connection, ct);
                await syncService.RetryPendingLocalBlocksAsync(connection.UserId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to renew Google watch for user {UserId}.", connection.UserId);
            }
        }
    }

    private async Task RemoveConnectionAsync(GoogleCalendarConnection connection, CancellationToken ct)
    {
        await StopWatchSafelyAsync(connection, ct);

        var mirrored = await calendarBlockRepository.GetByProviderAsync(connection.UserId, CalendarBlockProvider.Google);
        foreach (var block in mirrored)
        {
            await calendarBlockRepository.DeleteAsync(block);
            try { await notifier.CalendarBlockDeletedAsync(block.Id, connection.UserId); }
            catch (Exception ex) { logger.LogWarning(ex, "SignalR publish failed during disconnect cleanup."); }
        }

        await connectionRepository.DeleteAsync(connection);
    }

    private async Task StopWatchSafelyAsync(GoogleCalendarConnection connection, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connection.WatchChannelId)
            || string.IsNullOrWhiteSpace(connection.WatchResourceId))
            return;

        try { await client.StopWatchAsync(connection, connection.WatchChannelId, connection.WatchResourceId, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to stop Google Calendar watch channel."); }
    }

    private static void ResetSourceSelection(GoogleCalendarConnection connection, DateTimeOffset now)
    {
        connection.SourceCalendarId = null;
        connection.SyncToken = null;
        connection.WatchChannelId = null;
        connection.WatchResourceId = null;
        connection.WatchToken = null;
        connection.WatchExpiresAt = null;
        connection.LastSyncedAt = null;
        connection.UpdatedAt = now;
    }
}
