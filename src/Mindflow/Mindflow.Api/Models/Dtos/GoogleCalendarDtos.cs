namespace Mindflow.Api.Models.Dtos;

public record GoogleCalendarConnectResponse(string Url);

public record GoogleCalendarStatusResponse(
    bool Connected,
    string? Email,
    DateTimeOffset? ConnectedAt,
    bool PushEnabled,
    string? SourceCalendarId,
    bool RequiresReconnect,
    DateTimeOffset? WatchExpiresAt,
    DateTimeOffset? LastSyncedAt);

public record GoogleCalendarSyncResponse(int Changes, int Pushed);

public record SetSourceCalendarRequest(string CalendarId);
