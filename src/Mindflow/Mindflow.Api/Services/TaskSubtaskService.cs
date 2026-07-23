using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Task = System.Threading.Tasks.Task;

namespace Mindflow.Api.Services;

public class TaskSubtaskService(
    ITaskRepository taskRepository,
    ITaskSubtaskRepository subtaskRepository,
    ITaskTimeEntryRepository timeEntryRepository,
    ICurrentUserService currentUserService,
    IAccessService accessService,
    ITaskActivityService taskActivityService,
    ITasksNotifier notifier,
    ILogger<TaskSubtaskService> logger) : ITaskSubtaskService
{
    public async Task<TaskDetailResponse?> CreateAsync(Guid taskId, TaskSubtaskRequest request)
    {
        var task = await GetAccessibleTaskForCurrentUserAsync(taskId);
        if (task is null) return null;
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            var userId = await currentUserService.GetCurrentUserIdAsync();
            var loggedMinutes = await timeEntryRepository.GetDurationMinutesForTaskAsync(userId, task.Id);
            return TaskResponseMapper.ToDetailResponse(task, loggedMinutes);
        }

        var nextOrder = await subtaskRepository.GetNextSortOrderAsync(taskId);
        await subtaskRepository.CreateAsync(taskId, new TaskSubtask
        {
            Id = Guid.TryParse(request.Id, out var requestId) ? requestId : Guid.NewGuid(),
            Content = request.Content.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            IsCompleted = request.IsCompleted,
            DueDate = request.DueDate,
            SortOrder = nextOrder,
            CreatedAt = DateTimeOffset.UtcNow
        });

        return await ReturnUpdatedTaskAsync(taskId);
    }

    public async Task<TaskDetailResponse?> UpdateAsync(Guid taskId, Guid subtaskId, TaskSubtaskRequest request)
    {
        if (!await CanAccessTaskAsync(taskId)) return null;

        var subtask = await subtaskRepository.GetByIdForTaskAsync(taskId, subtaskId);
        if (subtask is null) return null;

        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            subtask.Content = request.Content.Trim();
        }

        subtask.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description;
        subtask.IsCompleted = request.IsCompleted;
        subtask.DueDate = request.DueDate;
        if (request.SortOrder.HasValue) subtask.SortOrder = request.SortOrder.Value;

        var updated = await subtaskRepository.UpdateAsync(subtask);
        return updated ? await ReturnUpdatedTaskAsync(taskId) : null;
    }

    public async Task<TaskDetailResponse?> DeleteAsync(Guid taskId, Guid subtaskId)
    {
        if (!await CanAccessTaskAsync(taskId)) return null;

        var deleted = await subtaskRepository.DeleteAsync(taskId, subtaskId);
        return deleted ? await ReturnUpdatedTaskAsync(taskId) : null;
    }

    public async Task<TaskDetailResponse?> ReorderAsync(Guid taskId, ReorderTaskSubtasksRequest request)
    {
        if (!await CanAccessTaskAsync(taskId)) return null;

        var reordered = await subtaskRepository.ReorderAsync(taskId, request.SubtaskIds);
        return reordered ? await ReturnUpdatedTaskAsync(taskId) : null;
    }

    private async Task<TaskItem?> GetAccessibleTaskForCurrentUserAsync(Guid taskId)
    {
        if (!await CanAccessTaskAsync(taskId)) return null;
        return await taskRepository.GetByIdAsync(taskId);
    }

    private async Task<bool> CanAccessTaskAsync(Guid taskId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await accessService.CanAccessTaskAsync(taskId, userId);
    }

    private async Task<TaskDetailResponse?> ReturnUpdatedTaskAsync(Guid taskId)
    {
        var updated = await taskRepository.GetByIdReadOnlyAsync(taskId);
        if (updated is null) return null;

        var userId = await currentUserService.GetCurrentUserIdAsync();
        var spaceId = await taskRepository.GetSpaceIdForTaskAsync(updated);

        await taskActivityService.RecordUserTaskEventAsync(
            TaskActivityEventType.TaskSubtasksChanged,
            userId,
            updated.Id,
            spaceId,
            updated.ProjectId,
            new
            {
                subtasks_count = updated.Subtasks.Count,
                completed_subtasks_count = updated.Subtasks.Count(s => s.IsCompleted),
                due_subtasks_count = updated.Subtasks.Count(s => s.DueDate.HasValue && !s.IsCompleted)
            });

        await NotifySafelyAsync(
            () => notifier.TaskUpdatedAsync(updated, spaceId),
            "TaskUpdated",
            updated.Id);

        var loggedMinutes = await timeEntryRepository.GetDurationMinutesForTaskAsync(userId, updated.Id);
        return TaskResponseMapper.ToDetailResponse(updated, loggedMinutes);
    }

    private async Task NotifySafelyAsync(Func<Task> publish, string eventName, Guid taskId)
    {
        try
        {
            await publish();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR publish failed for event {EventName} and task {TaskId}.", eventName, taskId);
        }
    }
}
