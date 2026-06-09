namespace Mindflow.Api.Models.Dtos;

public record GoogleCalendarConnectResponse(string Url);

public record GoogleCalendarStatusResponse(
    bool Connected,
    string? Email,
    DateTimeOffset? ConnectedAt,
    bool PushEnabled);

public record GoogleCalendarSyncResponse(int Changes);
