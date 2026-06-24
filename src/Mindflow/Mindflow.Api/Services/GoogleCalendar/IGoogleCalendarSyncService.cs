using Mindflow.Api.Models;

namespace Mindflow.Api.Services.GoogleCalendar;

public interface IGoogleCalendarSyncService
{
    /// <summary>Pull changes from Google and mirror them as read-only local blocks. Returns the number of applied changes.</summary>
    Task<int> SyncUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Best-effort push of a newly created local block to the dedicated Google calendar.</summary>
    Task PushBlockCreatedAsync(CalendarBlock block, CancellationToken ct = default);

    /// <summary>Best-effort push of an updated local block.</summary>
    Task PushBlockUpdatedAsync(CalendarBlock block, CancellationToken ct = default);

    /// <summary>Best-effort removal of a local block's mirrored Google event.</summary>
    Task PushBlockDeletedAsync(CalendarBlock block, CancellationToken ct = default);

    /// <summary>Retry local blocks that have not reached Google yet. Returns the number successfully pushed.</summary>
    Task<int> RetryPendingLocalBlocksAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Start (or renew) the push notification channel for a connection.</summary>
    Task EnsureWatchAsync(GoogleCalendarConnection connection, CancellationToken ct = default);
}
