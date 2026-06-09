using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

internal static class CalendarBlockMapper
{
    public static CalendarBlockResponse ToResponse(CalendarBlock block) =>
        new(
            block.Id,
            block.TaskId,
            block.UserId,
            block.Title,
            block.StartAt,
            block.DurationMinutes,
            block.CreatedAt,
            block.UpdatedAt,
            block.Provider,
            block.ExternalEventId,
            block.GoogleCalendarId,
            block.SyncStatus);
}
