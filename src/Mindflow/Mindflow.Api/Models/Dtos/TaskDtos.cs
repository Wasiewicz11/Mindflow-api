using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Models.Dtos;

public record CreateTaskRequest(
    [Required, MaxLength(1000)] string Content,
    [MaxLength(10000)] string? Description,
    TaskPriority? Priority,
    TaskStatus? Status,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    [Range(0.01, 1000)] decimal? EstimatedHours,
    Guid? ProjectId,
    IReadOnlyCollection<string>? Tags,
    IReadOnlyCollection<TaskSubtaskRequest>? Subtasks);

public record UpdateTaskRequest(
    [MaxLength(1000)] string? Content,
    [MaxLength(10000)] string? Description,
    TaskPriority? Priority,
    TaskStatus? Status,
    DateOnly? DueDate,
    bool ClearDueDate,
    TimeOnly? DueTime,
    bool ClearDueTime,
    [Range(0.01, 1000)] decimal? EstimatedHours,
    bool ClearEstimatedHours,
    Guid? ProjectId,
    IReadOnlyCollection<string>? Tags);

public record TaskSubtaskRequest(
    string? Id,
    [MaxLength(1000)] string Content,
    bool IsCompleted,
    TaskStatus? Status,
    [Range(0.01, 1000)] decimal? EstimatedHours,
    bool ClearEstimatedHours,
    [MaxLength(10000)] string? Description,
    DateOnly? DueDate,
    int? SortOrder);

public record ReorderTaskSubtasksRequest(IReadOnlyCollection<Guid> SubtaskIds);

public record TaskSubtaskResponse(
    Guid Id,
    string Content,
    bool IsCompleted,
    TaskStatus Status,
    string? Description,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    int LoggedMinutes,
    int SortOrder,
    DateTimeOffset CreatedAt);

public record TaskListResponse(
    Guid Id,
    string Content,
    bool IsCompleted,
    TaskPriority Priority,
    TaskStatus Status,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    decimal? EstimatedHours,
    int LoggedMinutes,
    Guid? ProjectId,
    IReadOnlyCollection<string> Tags,
    decimal? SubtasksEstimatedHours,
    int SubtasksLoggedMinutes,
    int SubtaskCompletedCount,
    int SubtaskTotalCount,
    int SubtaskDueCount,
    IReadOnlyCollection<TaskSubtaskResponse> DueSubtasks,
    IReadOnlyCollection<TaskSubtaskResponse> Subtasks,
    DateTimeOffset CreatedAt);

public record TaskDetailResponse(
    Guid Id,
    string Content,
    string? Description,
    bool IsCompleted,
    TaskPriority Priority,
    TaskStatus Status,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    decimal? EstimatedHours,
    int LoggedMinutes,
    Guid? ProjectId,
    IReadOnlyCollection<string> Tags,
    decimal? SubtasksEstimatedHours,
    int SubtasksLoggedMinutes,
    int SubtaskCompletedCount,
    int SubtaskTotalCount,
    int SubtaskDueCount,
    IReadOnlyCollection<TaskSubtaskResponse> DueSubtasks,
    IReadOnlyCollection<TaskSubtaskResponse> Subtasks,
    DateTimeOffset CreatedAt);
