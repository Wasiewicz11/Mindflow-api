using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class CalendarBlock
{
    public Guid Id { get; set; }
    public Guid? TaskId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(255)]
    public string? Title { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public int DurationMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public CalendarBlockProvider Provider { get; set; }
    [MaxLength(255)]
    public string? ExternalEventId { get; set; }
    [MaxLength(255)]
    public string? GoogleCalendarId { get; set; }
    public CalendarBlockSyncStatus SyncStatus { get; set; }
}
