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
    Guid? ProjectId);

public record UpdateTaskRequest(
    string? Content,
    [MaxLength(10000)] string? Description,
    TaskPriority? Priority,
    TaskStatus? Status,
    DateOnly? DueDate,
    bool ClearDueDate,
    Guid? ProjectId);

public record TaskListResponse(
    Guid Id,
    string Content,
    bool IsCompleted,
    TaskPriority Priority,
    TaskStatus Status,
    DateOnly? DueDate,
    Guid? ProjectId,
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
    DateTimeOffset CreatedAt);
