using Microsoft.Extensions.Options;
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

        // a fresh consent supersedes any previous connection
        var previous = await connectionRepository.GetByUserIdAsync(userId);
        if (previous is not null)
            await RemoveConnectionAsync(previous, ct);

        var now = DateTimeOffset.UtcNow;
        var connection = new GoogleCalendarConnection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GoogleAccountEmail = tokens.Email,
            RefreshTokenEncrypted = tokenProtector.Protect(tokens.RefreshToken),
            AccessTokenEncrypted = tokenProtector.Protect(tokens.AccessToken),
            AccessTokenExpiresAt = tokens.ExpiresAt,
            DedicatedCalendarId = string.Empty,
            SourceCalendarId = "primary",
            CreatedAt = now,
            UpdatedAt = now
        };

        connection.DedicatedCalendarId =
            await client.CreateDedicatedCalendarAsync(connection, _options.DedicatedCalendarName, ct);

        await connectionRepository.CreateAsync(connection);

        // initial mirror pull + start the push channel (no-op locally without a public webhook)
        await syncService.SyncUserAsync(userId, ct);
        await syncService.EnsureWatchAsync(connection, ct);

        return true;
    }

    public async Task<GoogleCalendarStatusResponse> GetStatusAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var connection = await connectionRepository.GetByUserIdAsync(userId);
        if (connection is null)
            return new GoogleCalendarStatusResponse(false, null, null, false);

        return new GoogleCalendarStatusResponse(
            true,
            connection.GoogleAccountEmail,
            connection.CreatedAt,
            connection.WatchChannelId is not null);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var connection = await connectionRepository.GetByUserIdAsync(userId);
        if (connection is null) return;
        await RemoveConnectionAsync(connection, ct);
    }

    public async Task<int> SyncCurrentUserAsync(CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await syncService.SyncUserAsync(userId, ct);
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
            try
            {
                await syncService.SyncUserAsync(connection.UserId, ct);
                await syncService.EnsureWatchAsync(connection, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to renew Google watch for user {UserId}.", connection.UserId);
            }
        }
    }

    private async Task RemoveConnectionAsync(GoogleCalendarConnection connection, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(connection.WatchChannelId) && !string.IsNullOrWhiteSpace(connection.WatchResourceId))
        {
            try { await client.StopWatchAsync(connection, connection.WatchChannelId, connection.WatchResourceId, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to stop Google watch on disconnect."); }
        }

        var mirrored = await calendarBlockRepository.GetByProviderAsync(connection.UserId, CalendarBlockProvider.Google);
        foreach (var block in mirrored)
        {
            await calendarBlockRepository.DeleteAsync(block);
            try { await notifier.CalendarBlockDeletedAsync(block.Id, connection.UserId); }
            catch (Exception ex) { logger.LogWarning(ex, "SignalR publish failed during disconnect cleanup."); }
        }

        await connectionRepository.DeleteAsync(connection);
    }
}
