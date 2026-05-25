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
    public bool IsCompleted { get; set; }
    public TaskPriority Priority { get; set; }
    public TaskStatus Status { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
