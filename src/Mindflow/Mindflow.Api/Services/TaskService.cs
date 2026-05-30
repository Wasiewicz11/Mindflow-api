using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Task = System.Threading.Tasks.Task;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Services;

public class TaskService(
    ITaskRepository taskRepository,
    ICurrentUserService currentUserService,
    IAccessService accessService,
    ITaskActivityService taskActivityService,
    ITasksNotifier notifier,
    ILogger<TaskService> logger
    ) : ITaskService
{
    public async Task<IEnumerable<TaskListResponse>> GetAllAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var tasks = await taskRepository.GetAllForUserAsync(userId);
        return tasks.Select(ToListResponse);
    }

    public async Task<IEnumerable<TaskListResponse>> GetAllForProjectAsync(Guid projectId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        if (!await accessService.CanAccessProjectAsync(projectId, userId))
            throw new UnauthorizedAccessException();

        var tasks = await taskRepository.GetAllForProjectAsync(projectId);
        return tasks.Select(ToListResponse);
    }

    public async Task<TaskDetailResponse?> GetByIdAsync(Guid id)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        if (!await accessService.CanAccessTaskAsync(id, userId))
            return null;

        var task = await taskRepository.GetByIdAsync(id);
        return task is null ? null : ToDetailResponse(task);
    }

    public async Task<TaskDetailResponse?> CreateAsync(CreateTaskRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        if (request.ProjectId.HasValue && !await accessService.CanAccessProjectAsync(request.ProjectId.Value, userId))
            return null;

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = request.ProjectId,
            Content = request.Content,
            Description = request.Description,
            IsCompleted = false,
            Priority = request.Priority ?? TaskPriority.P3,
            Status = request.Status ?? TaskStatus.NotStarted,
            DueDate = request.DueDate,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await taskRepository.CreateAsync(task);
        if (created is null) return null;

        var spaceId = await taskRepository.GetSpaceIdForTaskAsync(created);
        await taskActivityService.RecordUserTaskEventAsync(
            TaskActivityEventType.TaskCreated,
            userId,
            created.Id,
            spaceId,
            created.ProjectId,
            new
            {
                title_present = !string.IsNullOrWhiteSpace(created.Content),
                description_present = !string.IsNullOrWhiteSpace(created.Description),
                due_date = created.DueDate,
                priority = created.Priority.ToString(),
                status = created.Status.ToString(),
                project_id = created.ProjectId
            });

        await NotifySafelyAsync(
            () => notifier.TaskCreatedAsync(created, spaceId),
            "TaskCreated",
            created.Id);

        return ToDetailResponse(created);
    }

    public async Task<TaskDetailResponse?> UpdateAsync(Guid id, UpdateTaskRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        if (!await accessService.CanAccessTaskAsync(id, userId))
            throw new NotFoundException($"Task with id {id} not found");

        var task = await taskRepository.GetByIdAsync(id);
        if (task is null) throw new NotFoundException($"Task with id {id} not found");

        if (request.ProjectId.HasValue && !await accessService.CanAccessProjectAsync(request.ProjectId.Value, userId))
            return null;

        var previousSpaceId = await taskRepository.GetSpaceIdForTaskAsync(task);
        var previousContent = task.Content;
        var previousDescription = task.Description;
        var previousPriority = task.Priority;
        var previousDueDate = task.DueDate;
        var previousProjectId = task.ProjectId;
        var previousStatus = task.Status;

        if (request.Content is not null) task.Content = request.Content;
        if (request.Description is not null) task.Description = request.Description;
        if (request.Priority.HasValue) task.Priority = request.Priority.Value;
        if (request.DueDate.HasValue) task.DueDate = request.DueDate;
        if (request.ProjectId.HasValue) task.ProjectId = request.ProjectId;
        if (request.Status.HasValue) task.Status = request.Status.Value;

        var updated = await taskRepository.UpdateAsync(task);
        if (updated is null) return null;

        var currentSpaceId = await taskRepository.GetSpaceIdForTaskAsync(updated);
        await RecordTaskUpdateActivityAsync(
            userId,
            updated,
            previousSpaceId,
            currentSpaceId,
            previousContent,
            previousDescription,
            previousPriority,
            previousDueDate,
            previousProjectId,
            previousStatus);

        if (previousSpaceId.HasValue && previousSpaceId != currentSpaceId)
        {
            await NotifySafelyAsync(
                () => notifier.TaskRemovedFromSpaceAsync(updated.Id, previousSpaceId.Value),
                "TaskRemovedFromSpace",
                updated.Id);
        }

        await NotifySafelyAsync(
            () => notifier.TaskUpdatedAsync(updated, currentSpaceId),
            "TaskUpdated",
            updated.Id);

        return ToDetailResponse(updated);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        if (!await accessService.CanAccessTaskAsync(id, userId))
            return false;

        var task = await taskRepository.GetByIdAsync(id);
        if (task is null) return false;

        var spaceId = await taskRepository.GetSpaceIdForTaskAsync(task);

        var deleted = await taskRepository.DeleteAsync(id);
        if (!deleted) return false;

        await taskActivityService.RecordUserTaskEventAsync(
            TaskActivityEventType.TaskDeleted,
            userId,
            task.Id,
            spaceId,
            task.ProjectId,
            new
            {
                due_date = task.DueDate,
                priority = task.Priority.ToString(),
                status = task.Status.ToString(),
                project_id = task.ProjectId
            });

        await NotifySafelyAsync(
            () => notifier.TaskDeletedAsync(task.Id, task.UserId, spaceId),
            "TaskDeleted",
            task.Id);

        return true;
    }

    private static TaskListResponse ToListResponse(TaskItem t) =>
        new(t.Id, t.Content, t.IsCompleted, t.Priority, t.Status, t.DueDate, t.ProjectId, t.CreatedAt);

    private static TaskDetailResponse ToDetailResponse(TaskItem t) =>
        new(t.Id, t.Content, t.Description, t.IsCompleted, t.Priority, t.Status, t.DueDate, t.ProjectId, t.CreatedAt);

    private async Task RecordTaskUpdateActivityAsync(
        Guid userId,
        TaskItem updated,
        Guid? previousSpaceId,
        Guid? currentSpaceId,
        string previousContent,
        string? previousDescription,
        TaskPriority previousPriority,
        DateOnly? previousDueDate,
        Guid? previousProjectId,
        TaskStatus previousStatus)
    {
        if (updated.Content != previousContent)
        {
            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskTitleChanged,
                userId,
                updated.Id,
                currentSpaceId,
                updated.ProjectId,
                new
                {
                    previous_title_present = !string.IsNullOrWhiteSpace(previousContent),
                    new_title_present = !string.IsNullOrWhiteSpace(updated.Content)
                });
        }

        if (updated.Description != previousDescription)
        {
            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskDescriptionChanged,
                userId,
                updated.Id,
                currentSpaceId,
                updated.ProjectId,
                new
                {
                    previous_description_present = !string.IsNullOrWhiteSpace(previousDescription),
                    new_description_present = !string.IsNullOrWhiteSpace(updated.Description)
                });
        }

        if (updated.Priority != previousPriority)
        {
            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskPriorityChanged,
                userId,
                updated.Id,
                currentSpaceId,
                updated.ProjectId,
                new
                {
                    previous_priority = previousPriority.ToString(),
                    new_priority = updated.Priority.ToString()
                });
        }

        if (updated.DueDate != previousDueDate)
        {
            var eventType = previousDueDate.HasValue
                ? TaskActivityEventType.TaskDueDateChanged
                : TaskActivityEventType.TaskDueDateSet;

            await taskActivityService.RecordUserTaskEventAsync(
                eventType,
                userId,
                updated.Id,
                currentSpaceId,
                updated.ProjectId,
                new
                {
                    previous_due_date = previousDueDate,
                    new_due_date = updated.DueDate
                });

            if (previousDueDate.HasValue && updated.DueDate.HasValue && updated.DueDate.Value > previousDueDate.Value)
            {
                await taskActivityService.RecordUserTaskEventAsync(
                    TaskActivityEventType.TaskPostponed,
                    userId,
                    updated.Id,
                    currentSpaceId,
                    updated.ProjectId,
                    new
                    {
                        previous_due_date = previousDueDate,
                        new_due_date = updated.DueDate,
                        postponed_by_days = updated.DueDate.Value.DayNumber - previousDueDate.Value.DayNumber
                    });
            }
        }

        if (updated.ProjectId != previousProjectId)
        {
            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskProjectChanged,
                userId,
                updated.Id,
                currentSpaceId,
                updated.ProjectId,
                new
                {
                    previous_project_id = previousProjectId,
                    new_project_id = updated.ProjectId,
                    previous_space_id = previousSpaceId,
                    new_space_id = currentSpaceId
                });
        }

        if (updated.Status != previousStatus)
        {
            if (updated.Status == TaskStatus.Completed)
            {
                await taskActivityService.RecordUserTaskEventAsync(
                    TaskActivityEventType.TaskCompleted,
                    userId,
                    updated.Id,
                    currentSpaceId,
                    updated.ProjectId,
                    new
                    {
                        previous_status = previousStatus.ToString(),
                        new_status = updated.Status.ToString(),
                        due_date = updated.DueDate,
                        was_overdue = updated.DueDate.HasValue && updated.DueDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
                    });
            }
            else if (previousStatus == TaskStatus.Completed)
            {
                await taskActivityService.RecordUserTaskEventAsync(
                    TaskActivityEventType.TaskReopened,
                    userId,
                    updated.Id,
                    currentSpaceId,
                    updated.ProjectId,
                    new
                    {
                        previous_status = previousStatus.ToString(),
                        new_status = updated.Status.ToString()
                    });
            }
        }
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
