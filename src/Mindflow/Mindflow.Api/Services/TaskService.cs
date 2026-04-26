using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Task = System.Threading.Tasks.Task;

namespace Mindflow.Api.Services;

public class TaskService(
    ITaskRepository taskRepository,
    ICurrentUserService currentUserService,
    ITasksNotifier notifier,
    ILogger<TaskService> logger
    ) : ITaskService
{
    public async Task<IEnumerable<TaskItem>> GetAllForCurrentUserAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await taskRepository.GetAllForUserAsync(userId);
    }

    public async Task<TaskItem?> GetByIdForCurrentUserAsync(Guid id)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await taskRepository.GetByIdForUserAsync(id, userId);
    }

    public async Task<TaskItem?> CreateForCurrentUserAsync(CreateTaskRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = request.ProjectId,
            Content = request.Content,
            IsCompleted = false,
            Priority = request.Priority ?? TaskPriority.P3,
            DueDate = request.DueDate,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await taskRepository.CreateForUserAsync(task, userId);
        if (created is null) return null;

        var spaceId = await taskRepository.GetSpaceIdForTaskAsync(created);
        await NotifySafelyAsync(
            () => notifier.TaskCreatedAsync(created, spaceId),
            "TaskCreated",
            created.Id);

        return created;
    }

    public async Task<TaskItem?> UpdateForCurrentUserAsync(Guid id, UpdateTaskRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var task = await taskRepository.GetByIdForUserAsync(id, userId);

        if (task is null)
            throw new NotFoundException($"Task with id {id} not found");

        var previousSpaceId = await taskRepository.GetSpaceIdForTaskAsync(task);

        if (request.Content is not null) task.Content = request.Content;
        if (request.Priority.HasValue) task.Priority = request.Priority.Value;
        if (request.DueDate.HasValue) task.DueDate = request.DueDate;
        if (request.ProjectId.HasValue) task.ProjectId = request.ProjectId;

        var updated = await taskRepository.UpdateForUserAsync(task, userId);
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

        return updated;
    }

    public async Task<bool> DeleteForCurrentUserAsync(Guid id)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        var task = await taskRepository.GetByIdForUserAsync(id, userId);
        if (task is null) return false;

        var spaceId = await taskRepository.GetSpaceIdForTaskAsync(task);

        var deleted = await taskRepository.DeleteForUserAsync(id, userId);
        if (!deleted) return false;

        await NotifySafelyAsync(
            () => notifier.TaskDeletedAsync(task.Id, task.UserId, spaceId),
            "TaskDeleted",
            task.Id);

        return true;
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
