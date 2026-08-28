using System.ComponentModel.DataAnnotations;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Models;

public class TaskSubtask
{
    public Guid Id { get; set; }
    public Guid TaskItemId { get; set; }
    [MaxLength(1000)]
    public required string Content { get; set; }
    [MaxLength(10000)]
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public TaskStatus Status { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? EstimatedHours { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
