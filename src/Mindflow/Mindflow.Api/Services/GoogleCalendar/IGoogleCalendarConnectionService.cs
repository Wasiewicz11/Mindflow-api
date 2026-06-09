using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.GoogleCalendar;

public interface IGoogleCalendarConnectionService
{
    /// <summary>Build the Google consent URL for the current user.</summary>
    Task<string> BeginConnectAsync();

    /// <summary>Finish the OAuth code exchange for the user encoded in the state. Returns false when no refresh token was granted.</summary>
    Task<bool> CompleteConnectAsync(string code, Guid userId, CancellationToken ct = default);

    Task<GoogleCalendarStatusResponse> GetStatusAsync();

    Task DisconnectAsync(CancellationToken ct = default);

    Task<int> SyncCurrentUserAsync(CancellationToken ct = default);

    Task HandleWebhookAsync(string? channelId, string? token, string? resourceState, CancellationToken ct = default);

    Task RenewExpiringWatchesAsync(CancellationToken ct = default);
}
