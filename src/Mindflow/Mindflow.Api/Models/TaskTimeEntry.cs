using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Models;

public class TaskTimeEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ProjectId { get; set; }
    [MaxLength(1000)]
    public required string TaskContent { get; set; }
    public TaskPriority TaskPriority { get; set; }
    public TaskStatus TaskStatus { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateOnly WorkDate { get; set; }
    public int DurationMinutes { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public decimal? EstimatedHours { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
