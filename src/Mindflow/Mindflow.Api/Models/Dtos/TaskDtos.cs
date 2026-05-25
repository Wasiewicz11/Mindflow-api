using Mindflow.Api.Models.Enums;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Models.Dtos;

public record CreateTaskRequest(
    string Content,
    TaskPriority? Priority,
    TaskStatus? Status,
    DateOnly? DueDate,
    Guid? ProjectId);

public record UpdateTaskRequest(
    string? Content,
    TaskPriority? Priority,
    TaskStatus? Status,
    DateOnly? DueDate,
    Guid? ProjectId);
