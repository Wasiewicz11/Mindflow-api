using Mindflow.Api.Models;

namespace Mindflow.Api.Services.GoogleCalendar;

public record GoogleTokenExchangeResult(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string Email);

public record GoogleEventChange(string EventId, bool IsDeleted, string? Title, DateTimeOffset Start, int DurationMinutes);

public record GoogleSyncResult(IReadOnlyList<GoogleEventChange> Changes, string? NewSyncToken);

public record GoogleWatchResult(string ResourceId, DateTimeOffset? ExpiresAt);

public record GoogleCalendarListEntry(string Id, string? Summary, bool Primary, string? BackgroundColor);

/// <summary>Thrown when an incremental sync token is no longer valid (HTTP 410) and a full resync is required.</summary>
public class GoogleSyncTokenExpiredException : Exception;

/// <summary>All direct interaction with the Google Calendar API lives behind this interface.</summary>
public interface IGoogleCalendarClient
{
    string BuildConsentUrl(string state);

    Task<GoogleTokenExchangeResult> ExchangeCodeAsync(string code, CancellationToken ct);

    Task<string> CreateDedicatedCalendarAsync(GoogleCalendarConnection connection, string calendarName, CancellationToken ct);

    /// <summary>Lists the calendars the connected Google account can read (for the source-calendar picker).</summary>
    Task<IReadOnlyList<GoogleCalendarListEntry>> ListCalendarsAsync(GoogleCalendarConnection connection, CancellationToken ct);

    /// <summary>Insert (when externalEventId is null) or patch the Google event for a local block. Returns the Google event id.</summary>
    Task<string> UpsertEventAsync(GoogleCalendarConnection connection, CalendarBlock block, CancellationToken ct);

    Task DeleteEventAsync(GoogleCalendarConnection connection, string calendarId, string externalEventId, CancellationToken ct);

    /// <summary>List changes from the user's primary calendar. Pass syncToken=null for a full window sync.</summary>
    Task<GoogleSyncResult> ListChangesAsync(GoogleCalendarConnection connection, string? syncToken, CancellationToken ct);

    Task<GoogleWatchResult> StartWatchAsync(GoogleCalendarConnection connection, string channelId, string channelToken, string webhookUrl, CancellationToken ct);

    Task StopWatchAsync(GoogleCalendarConnection connection, string channelId, string resourceId, CancellationToken ct);
}
