using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Models;

public class TaskItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ProjectId { get; set; }
    [MaxLength(1000)]
    public required string Content { get; set; }
    [MaxLength(10000)]
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public TaskPriority Priority { get; set; }
    public TaskStatus Status { get; set; }
    public DateOnly? DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public decimal? EstimatedHours { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<TaskSubtask> Subtasks { get; set; } = new();
    /// <summary>Present only on the source task shown in the "Cykliczne" section.</summary>
    public TaskRecurrence? Recurrence { get; set; }
    /// <summary>Points from a generated occurrence to its recurring source task.</summary>
    public Guid? RecurringTaskId { get; set; }
    public TaskItem? RecurringTask { get; set; }
    public List<TaskItem> RecurringInstances { get; set; } = new();
    /// <summary>The local calendar date represented by a generated occurrence.</summary>
    public DateOnly? RecurrenceOccurrenceDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
