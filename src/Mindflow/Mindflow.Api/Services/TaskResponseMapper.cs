using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

internal static class TaskResponseMapper
{
    public static TaskListResponse ToListResponse(TaskItem task) =>
        new(
            task.Id,
            task.Content,
            task.IsCompleted,
            task.Priority,
            task.Status,
            task.DueDate,
            task.EstimatedHours,
            task.ProjectId,
            task.Tags.ToArray(),
            task.Subtasks.Count(s => s.IsCompleted),
            task.Subtasks.Count,
            GetDueSubtasks(task).Length,
            GetDueSubtasks(task),
            task.CreatedAt);

    public static TaskDetailResponse ToDetailResponse(TaskItem task) =>
        new(
            task.Id,
            task.Content,
            task.Description,
            task.IsCompleted,
            task.Priority,
            task.Status,
            task.DueDate,
            task.EstimatedHours,
            task.ProjectId,
            task.Tags.ToArray(),
            task.Subtasks.Count(s => s.IsCompleted),
            task.Subtasks.Count,
            GetDueSubtasks(task).Length,
            GetDueSubtasks(task),
            task.Subtasks
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.CreatedAt)
                .Select(ToSubtaskResponse)
                .ToArray(),
            task.CreatedAt);

    private static TaskSubtaskResponse ToSubtaskResponse(TaskSubtask subtask) =>
        new(
            subtask.Id,
            subtask.Content,
            subtask.IsCompleted,
            subtask.Description,
            subtask.DueDate,
            subtask.SortOrder,
            subtask.CreatedAt);

    private static TaskSubtaskResponse[] GetDueSubtasks(TaskItem task) =>
        task.Subtasks
            .Where(s => !s.IsCompleted && s.DueDate.HasValue)
            .OrderBy(s => s.DueDate)
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAt)
            .Select(ToSubtaskResponse)
            .ToArray();
}
