using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Models.Dtos;

public record CreateTaskRequest(
    string Content,
    [MaxLength(10000)] string? Description,
    TaskPriority? Priority,
    TaskStatus? Status,
    DateOnly? DueDate,
    Guid? ProjectId,
    IReadOnlyCollection<string>? Tags,
    IReadOnlyCollection<TaskSubtaskRequest>? Subtasks);

public record UpdateTaskRequest(
    string? Content,
    [MaxLength(10000)] string? Description,
    TaskPriority? Priority,
    TaskStatus? Status,
    DateOnly? DueDate,
    bool ClearDueDate,
    Guid? ProjectId,
    IReadOnlyCollection<string>? Tags,
    IReadOnlyCollection<TaskSubtaskRequest>? Subtasks);

public record TaskSubtaskRequest(
    string? Id,
    string Content,
    bool IsCompleted,
    [MaxLength(10000)] string? Description,
    DateOnly? DueDate,
    int? SortOrder);

public record ReorderTaskSubtasksRequest(IReadOnlyCollection<Guid> SubtaskIds);

public record TaskSubtaskResponse(
    Guid Id,
    string Content,
    bool IsCompleted,
    string? Description,
    DateOnly? DueDate,
    int SortOrder,
    DateTimeOffset CreatedAt);

public record TaskListResponse(
    Guid Id,
    string Content,
    bool IsCompleted,
    TaskPriority Priority,
    TaskStatus Status,
    DateOnly? DueDate,
    Guid? ProjectId,
    IReadOnlyCollection<string> Tags,
    int SubtaskCompletedCount,
    int SubtaskTotalCount,
    int SubtaskDueCount,
    IReadOnlyCollection<TaskSubtaskResponse> DueSubtasks,
    DateTimeOffset CreatedAt);

public record TaskDetailResponse(
    Guid Id,
    string Content,
    string? Description,
    bool IsCompleted,
    TaskPriority Priority,
    TaskStatus Status,
    DateOnly? DueDate,
    Guid? ProjectId,
    IReadOnlyCollection<string> Tags,
    int SubtaskCompletedCount,
    int SubtaskTotalCount,
    int SubtaskDueCount,
    IReadOnlyCollection<TaskSubtaskResponse> DueSubtasks,
    IReadOnlyCollection<TaskSubtaskResponse> Subtasks,
    DateTimeOffset CreatedAt);
