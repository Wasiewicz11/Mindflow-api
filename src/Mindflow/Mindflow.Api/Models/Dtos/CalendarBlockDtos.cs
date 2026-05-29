using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models.Dtos;

public record CreateCalendarBlockRequest(
    Guid TaskId,
    DateTimeOffset StartAt,
    int DurationMinutes);

public record UpdateCalendarBlockRequest(
    Guid TaskId,
    DateTimeOffset StartAt,
    int DurationMinutes);

public record CalendarBlockResponse(
    Guid Id,
    Guid TaskId,
    Guid UserId,
    DateTimeOffset StartAt,
    int DurationMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    CalendarBlockProvider Provider,
    string? ExternalEventId,
    string? GoogleCalendarId,
    CalendarBlockSyncStatus SyncStatus);
