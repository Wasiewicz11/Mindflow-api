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
    IProjectTagRepository projectTagRepository,
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

        var tags = NormalizeTags(request.Tags);
        if (request.ProjectId.HasValue && tags.Count > 0)
        {
            tags = (await projectTagRepository.EnsureExistAsync(request.ProjectId.Value, tags)).ToList();
        }
        else if (!request.ProjectId.HasValue)
        {
            tags.Clear();
        }

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
            Tags = tags,
            Subtasks = NormalizeSubtasks(request.Subtasks),
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
                project_id = created.ProjectId,
                tags_count = created.Tags.Count,
                subtasks_count = created.Subtasks.Count
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
        var previousTags = task.Tags.ToList();
        var previousSubtasks = task.Subtasks.Select(ToSubtaskResponse).ToArray();

        if (request.Content is not null) task.Content = request.Content;
        if (request.Description is not null) task.Description = request.Description;
        if (request.Priority.HasValue) task.Priority = request.Priority.Value;
        if (request.ClearDueDate) task.DueDate = null;
        else if (request.DueDate.HasValue) task.DueDate = request.DueDate;
        if (request.ProjectId.HasValue) task.ProjectId = request.ProjectId;
        if (request.Status.HasValue) task.Status = request.Status.Value;
        if (request.Tags is not null) task.Tags = NormalizeTags(request.Tags);
        if (request.Subtasks is not null)
        {
            ApplySubtasks(task.Subtasks, request.Subtasks);
        }

        // Sync task tags into the (possibly new) project's tag pool.
        // Covers tag edits as well as moves between projects (copy all into target).
        if (task.ProjectId.HasValue && task.Tags.Count > 0)
        {
            task.Tags = (await projectTagRepository.EnsureExistAsync(task.ProjectId.Value, task.Tags)).ToList();
        }
        else if (!task.ProjectId.HasValue)
        {
            task.Tags.Clear();
        }

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
            previousStatus,
            previousTags,
            previousSubtasks);

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
        new(
            t.Id,
            t.Content,
            t.IsCompleted,
            t.Priority,
            t.Status,
            t.DueDate,
            t.ProjectId,
            t.Tags.ToArray(),
            t.Subtasks.Count(s => s.IsCompleted),
            t.Subtasks.Count,
            GetDueSubtasks(t).Length,
            GetDueSubtasks(t),
            t.CreatedAt);

    private static TaskDetailResponse ToDetailResponse(TaskItem t) =>
        new(
            t.Id,
            t.Content,
            t.Description,
            t.IsCompleted,
            t.Priority,
            t.Status,
            t.DueDate,
            t.ProjectId,
            t.Tags.ToArray(),
            t.Subtasks.Count(s => s.IsCompleted),
            t.Subtasks.Count,
            GetDueSubtasks(t).Length,
            GetDueSubtasks(t),
            t.Subtasks
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.CreatedAt)
                .Select(ToSubtaskResponse)
                .ToArray(),
            t.CreatedAt);

    private static TaskSubtaskResponse ToSubtaskResponse(TaskSubtask s) =>
        new(s.Id, s.Content, s.IsCompleted, s.Description, s.DueDate, s.SortOrder, s.CreatedAt);

    private static TaskSubtaskResponse[] GetDueSubtasks(TaskItem t) =>
        t.Subtasks
            .Where(s => !s.IsCompleted && s.DueDate.HasValue)
            .OrderBy(s => s.DueDate)
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAt)
            .Select(ToSubtaskResponse)
            .ToArray();

    private static List<string> NormalizeTags(IReadOnlyCollection<string>? tags)
    {
        if (tags is null) return new List<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var trimmed = raw.Trim();
            if (seen.Add(trimmed)) result.Add(trimmed);
        }
        return result;
    }

    private static List<TaskSubtask> NormalizeSubtasks(IReadOnlyCollection<TaskSubtaskRequest>? subtasks)
    {
        if (subtasks is null) return new List<TaskSubtask>();

        var result = new List<TaskSubtask>();
        var index = 0;
        foreach (var raw in subtasks)
        {
            if (string.IsNullOrWhiteSpace(raw.Content))
            {
                index++;
                continue;
            }

            var id = Guid.TryParse(raw.Id, out var parsedId) ? parsedId : Guid.NewGuid();
            result.Add(new TaskSubtask
            {
                Id = id,
                Content = raw.Content.Trim(),
                Description = string.IsNullOrWhiteSpace(raw.Description) ? null : raw.Description,
                IsCompleted = raw.IsCompleted,
                DueDate = raw.DueDate,
                SortOrder = raw.SortOrder ?? index,
                CreatedAt = DateTimeOffset.UtcNow
            });
            index++;
        }

        return result;
    }

    private static void ApplySubtasks(ICollection<TaskSubtask> current, IReadOnlyCollection<TaskSubtaskRequest> subtasks)
    {
        var remaining = current.ToDictionary(s => s.Id);
        var index = 0;

        foreach (var raw in subtasks)
        {
            if (string.IsNullOrWhiteSpace(raw.Content))
            {
                index++;
                continue;
            }

            TaskSubtask? subtask = null;
            var hasExistingId = Guid.TryParse(raw.Id, out var parsedId) && remaining.TryGetValue(parsedId, out subtask);
            if (!hasExistingId)
            {
                subtask = new TaskSubtask
                {
                    Id = Guid.TryParse(raw.Id, out var newId) ? newId : Guid.NewGuid(),
                    Content = raw.Content.Trim(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                current.Add(subtask);
            }
            else
            {
                remaining.Remove(parsedId);
            }

            subtask!.Content = raw.Content.Trim();
            subtask.Description = string.IsNullOrWhiteSpace(raw.Description) ? null : raw.Description;
            subtask.IsCompleted = raw.IsCompleted;
            subtask.DueDate = raw.DueDate;
            subtask.SortOrder = raw.SortOrder ?? index;
            index++;
        }

        foreach (var removed in remaining.Values)
        {
            current.Remove(removed);
        }
    }

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
        TaskStatus previousStatus,
        IReadOnlyCollection<string> previousTags,
        IReadOnlyCollection<TaskSubtaskResponse> previousSubtasks)
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
            var eventType = (previousDueDate.HasValue, updated.DueDate.HasValue) switch
            {
                (false, true) => TaskActivityEventType.TaskDueDateSet,
                (true, false) => TaskActivityEventType.TaskDueDateRemoved,
                _ => TaskActivityEventType.TaskDueDateChanged
            };

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

        if (!TagListsEqual(previousTags, updated.Tags))
        {
            var previousSet = new HashSet<string>(previousTags, StringComparer.Ordinal);
            var currentSet = new HashSet<string>(updated.Tags, StringComparer.Ordinal);
            var added = updated.Tags.Where(t => !previousSet.Contains(t)).ToArray();
            var removed = previousTags.Where(t => !currentSet.Contains(t)).ToArray();

            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskTagsChanged,
                userId,
                updated.Id,
                currentSpaceId,
                updated.ProjectId,
                new
                {
                    previous_tags = previousTags,
                    new_tags = updated.Tags,
                    added_tags = added,
                    removed_tags = removed
                });
        }

        var currentSubtasks = updated.Subtasks
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAt)
            .Select(ToSubtaskResponse)
            .ToArray();
        if (!SubtaskListsEqual(previousSubtasks, currentSubtasks))
        {
            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskSubtasksChanged,
                userId,
                updated.Id,
                currentSpaceId,
                updated.ProjectId,
                new
                {
                    previous_subtasks_count = previousSubtasks.Count,
                    new_subtasks_count = currentSubtasks.Length,
                    completed_subtasks_count = currentSubtasks.Count(s => s.IsCompleted),
                    due_subtasks_count = currentSubtasks.Count(s => s.DueDate.HasValue && !s.IsCompleted)
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

    private static bool TagListsEqual(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count != b.Count) return false;
        using var ea = a.GetEnumerator();
        using var eb = b.GetEnumerator();
        while (ea.MoveNext() && eb.MoveNext())
        {
            if (!string.Equals(ea.Current, eb.Current, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool SubtaskListsEqual(IReadOnlyCollection<TaskSubtaskResponse> a, IReadOnlyCollection<TaskSubtaskResponse> b)
    {
        if (a.Count != b.Count) return false;
        using var ea = a.GetEnumerator();
        using var eb = b.GetEnumerator();
        while (ea.MoveNext() && eb.MoveNext())
        {
            if (ea.Current.Id != eb.Current.Id
                || ea.Current.Content != eb.Current.Content
                || ea.Current.Description != eb.Current.Description
                || ea.Current.IsCompleted != eb.Current.IsCompleted
                || ea.Current.DueDate != eb.Current.DueDate
                || ea.Current.SortOrder != eb.Current.SortOrder)
                return false;
        }
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
