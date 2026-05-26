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

        if (request.Content is not null) task.Content = request.Content;
        if (request.Description is not null) task.Description = request.Description;
        if (request.Priority.HasValue) task.Priority = request.Priority.Value;
        if (request.DueDate.HasValue) task.DueDate = request.DueDate;
        if (request.ProjectId.HasValue) task.ProjectId = request.ProjectId;
        if (request.Status.HasValue) task.Status = request.Status.Value;

        var updated = await taskRepository.UpdateAsync(task);
        if (updated is null) return null;

        var currentSpaceId = await taskRepository.GetSpaceIdForTaskAsync(updated);

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
