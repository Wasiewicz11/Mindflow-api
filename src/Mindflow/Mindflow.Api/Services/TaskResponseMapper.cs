using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

internal static class TaskResponseMapper
{
    public static TaskListResponse ToListResponse(
        TaskItem task,
        int loggedMinutes = 0,
        IReadOnlyDictionary<Guid, int>? subtaskLoggedMinutes = null) =>
        new(
            task.Id,
            task.Content,
            task.IsCompleted,
            task.Priority,
            task.Status,
            task.DueDate,
            task.DueTime,
            task.EstimatedHours,
            loggedMinutes,
            task.ProjectId,
            task.Tags.ToArray(),
            SumSubtaskEstimates(task),
            SumSubtaskLogged(task, subtaskLoggedMinutes),
            task.Subtasks.Count(s => s.IsCompleted),
            task.Subtasks.Count,
            GetDueSubtasks(task, subtaskLoggedMinutes).Length,
            GetDueSubtasks(task, subtaskLoggedMinutes),
            GetOrderedSubtasks(task, subtaskLoggedMinutes),
            task.CreatedAt);

    public static TaskDetailResponse ToDetailResponse(
        TaskItem task,
        int loggedMinutes = 0,
        IReadOnlyDictionary<Guid, int>? subtaskLoggedMinutes = null) =>
        new(
            task.Id,
            task.Content,
            task.Description,
            task.IsCompleted,
            task.Priority,
            task.Status,
            task.DueDate,
            task.DueTime,
            task.EstimatedHours,
            loggedMinutes,
            task.ProjectId,
            task.Tags.ToArray(),
            SumSubtaskEstimates(task),
            SumSubtaskLogged(task, subtaskLoggedMinutes),
            task.Subtasks.Count(s => s.IsCompleted),
            task.Subtasks.Count,
            GetDueSubtasks(task, subtaskLoggedMinutes).Length,
            GetDueSubtasks(task, subtaskLoggedMinutes),
            GetOrderedSubtasks(task, subtaskLoggedMinutes),
            task.CreatedAt);

    private static TaskSubtaskResponse ToSubtaskResponse(
        TaskSubtask subtask,
        IReadOnlyDictionary<Guid, int>? loggedMinutes) =>
        new(
            subtask.Id,
            subtask.Content,
            subtask.IsCompleted,
            subtask.Status,
            subtask.Description,
            subtask.DueDate,
            subtask.EstimatedHours,
            loggedMinutes is not null && loggedMinutes.TryGetValue(subtask.Id, out var minutes) ? minutes : 0,
            subtask.SortOrder,
            subtask.CreatedAt);

    /// <summary>Kept separate from the task's own estimate: the two can legitimately disagree.</summary>
    private static decimal? SumSubtaskEstimates(TaskItem task)
    {
        var estimates = task.Subtasks.Where(s => s.EstimatedHours.HasValue).ToArray();
        return estimates.Length == 0 ? null : estimates.Sum(s => s.EstimatedHours!.Value);
    }

    private static int SumSubtaskLogged(TaskItem task, IReadOnlyDictionary<Guid, int>? loggedMinutes)
    {
        if (loggedMinutes is null) return 0;
        return task.Subtasks.Sum(s => loggedMinutes.TryGetValue(s.Id, out var minutes) ? minutes : 0);
    }

    private static TaskSubtaskResponse[] GetDueSubtasks(
        TaskItem task,
        IReadOnlyDictionary<Guid, int>? loggedMinutes = null) =>
        task.Subtasks
            .Where(s => !s.IsCompleted && s.DueDate.HasValue)
            .OrderBy(s => s.DueDate)
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAt)
            .Select(subtask => ToSubtaskResponse(subtask, loggedMinutes))
            .ToArray();

    private static TaskSubtaskResponse[] GetOrderedSubtasks(
        TaskItem task,
        IReadOnlyDictionary<Guid, int>? loggedMinutes = null) =>
        task.Subtasks
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAt)
            .Select(subtask => ToSubtaskResponse(subtask, loggedMinutes))
            .ToArray();
}
