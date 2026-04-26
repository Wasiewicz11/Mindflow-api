using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models.Dtos;

public record CreateTaskRequest(
    string Content,
    TaskPriority? Priority,
    DateOnly? DueDate,
    Guid? ProjectId);

public record UpdateTaskRequest(
    string? Content,
    TaskPriority? Priority,
    DateOnly? DueDate,
    Guid? ProjectId);
