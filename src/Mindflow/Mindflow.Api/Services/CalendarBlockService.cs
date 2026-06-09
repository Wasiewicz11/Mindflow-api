using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services.GoogleCalendar;
using Task = System.Threading.Tasks.Task;

namespace Mindflow.Api.Services;

public class CalendarBlockService(
    ICalendarBlockRepository calendarBlockRepository,
    ITaskRepository taskRepository,
    ICurrentUserService currentUserService,
    IAccessService accessService,
    ITaskActivityService taskActivityService,
    ITasksNotifier notifier,
    IGoogleCalendarSyncService googleCalendarSync,
    ILogger<CalendarBlockService> logger) : ICalendarBlockService
{
    public async Task<IEnumerable<CalendarBlockResponse>> GetAsync(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new BadRequestException("The 'from' date cannot be later than the 'to' date.");

        var userId = await currentUserService.GetCurrentUserIdAsync();
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var blocks = await calendarBlockRepository.GetForUserInRangeAsync(userId, fromUtc, toUtc);
        return blocks.Select(ToResponse);
    }

    public async Task<CalendarBlockResponse?> CreateAsync(CreateCalendarBlockRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var title = NormalizeTitle(request.Title);
        if (request.TaskId is null && title is null)
            throw new BadRequestException("Calendar block title is required when no task is assigned.");

        if (request.TaskId is Guid taskId && !await accessService.CanAccessTaskAsync(taskId, userId))
            return null;

        var now = DateTimeOffset.UtcNow;
        var block = new CalendarBlock
        {
            Id = Guid.NewGuid(),
            TaskId = request.TaskId,
            UserId = userId,
            Title = title,
            StartAt = request.StartAt.ToUniversalTime(),
            DurationMinutes = request.DurationMinutes,
            CreatedAt = now,
            UpdatedAt = now,
            Provider = CalendarBlockProvider.Local,
            SyncStatus = CalendarBlockSyncStatus.Local
        };

        var created = await calendarBlockRepository.CreateAsync(block);
        var response = ToResponse(created);

        if (created.TaskId is Guid createdTaskId)
        {
            var taskContext = await GetTaskActivityContextAsync(createdTaskId);

            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskTimeSet,
                userId,
                createdTaskId,
                taskContext.SpaceId,
                taskContext.ProjectId,
                new
                {
                    calendar_block_id = created.Id,
                    start_at = created.StartAt,
                    duration_minutes = created.DurationMinutes
                });
        }

        await NotifySafelyAsync(
            () => notifier.CalendarBlockCreatedAsync(response),
            "CalendarBlockCreated",
            created.Id);

        await googleCalendarSync.PushBlockCreatedAsync(created);

        return response;
    }

    public async Task<CalendarBlockResponse?> UpdateAsync(Guid id, UpdateCalendarBlockRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var block = await calendarBlockRepository.GetByIdAsync(id);

        if (block is null || block.UserId != userId)
            throw new NotFoundException($"Calendar block with id {id} not found");

        if (block.Provider != CalendarBlockProvider.Local)
            throw new BadRequestException("Google calendar events are read-only and cannot be edited here.");

        var title = NormalizeTitle(request.Title);
        if (request.TaskId is null && title is null)
            throw new BadRequestException("Calendar block title is required when no task is assigned.");

        if (request.TaskId is Guid taskId && !await accessService.CanAccessTaskAsync(taskId, userId))
            return null;

        var previousTaskId = block.TaskId;
        var previousStartAt = block.StartAt;
        var previousDurationMinutes = block.DurationMinutes;
        var newStartAt = request.StartAt.ToUniversalTime();

        block.TaskId = request.TaskId;
        block.Title = title;
        block.StartAt = newStartAt;
        block.DurationMinutes = request.DurationMinutes;
        block.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await calendarBlockRepository.UpdateAsync(block);
        var response = ToResponse(updated);
        await RecordCalendarBlockUpdateActivityAsync(
            userId,
            updated,
            previousTaskId,
            previousStartAt,
            previousDurationMinutes);

        await NotifySafelyAsync(
            () => notifier.CalendarBlockUpdatedAsync(response),
            "CalendarBlockUpdated",
            updated.Id);

        await googleCalendarSync.PushBlockUpdatedAsync(updated);

        return response;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var block = await calendarBlockRepository.GetByIdAsync(id);

        if (block is null || block.UserId != userId)
            return false;

        if (block.Provider != CalendarBlockProvider.Local)
            return false;

        var deleted = await calendarBlockRepository.DeleteAsync(block);
        if (!deleted) return false;

        await googleCalendarSync.PushBlockDeletedAsync(block);

        if (block.TaskId is Guid taskId)
        {
            var taskContext = await GetTaskActivityContextAsync(taskId);
            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskTimeRemoved,
                userId,
                taskId,
                taskContext.SpaceId,
                taskContext.ProjectId,
                new
                {
                    calendar_block_id = block.Id,
                    previous_start_at = block.StartAt,
                    previous_duration_minutes = block.DurationMinutes
                });
        }

        await NotifySafelyAsync(
            () => notifier.CalendarBlockDeletedAsync(block.Id, block.UserId),
            "CalendarBlockDeleted",
            block.Id);

        return true;
    }

    private static CalendarBlockResponse ToResponse(CalendarBlock block) =>
        CalendarBlockMapper.ToResponse(block);

    private async Task RecordCalendarBlockUpdateActivityAsync(
        Guid userId,
        CalendarBlock updated,
        Guid? previousTaskId,
        DateTimeOffset previousStartAt,
        int previousDurationMinutes)
    {
        if (updated.TaskId != previousTaskId)
        {
            if (previousTaskId is Guid oldTaskId)
            {
                var previousTaskContext = await GetTaskActivityContextAsync(oldTaskId);
                await taskActivityService.RecordUserTaskEventAsync(
                    TaskActivityEventType.TaskTimeRemoved,
                    userId,
                    oldTaskId,
                    previousTaskContext.SpaceId,
                    previousTaskContext.ProjectId,
                    new
                    {
                        calendar_block_id = updated.Id,
                        previous_start_at = previousStartAt,
                        previous_duration_minutes = previousDurationMinutes,
                        moved_to_task_id = updated.TaskId
                    });
            }

            if (updated.TaskId is Guid newTaskId)
            {
                var currentTaskContext = await GetTaskActivityContextAsync(newTaskId);
                await taskActivityService.RecordUserTaskEventAsync(
                    TaskActivityEventType.TaskTimeSet,
                    userId,
                    newTaskId,
                    currentTaskContext.SpaceId,
                    currentTaskContext.ProjectId,
                    new
                    {
                        calendar_block_id = updated.Id,
                        start_at = updated.StartAt,
                        duration_minutes = updated.DurationMinutes,
                        moved_from_task_id = previousTaskId
                    });
            }

            return;
        }

        if (updated.TaskId is not Guid updatedTaskId)
            return;

        if (updated.StartAt == previousStartAt && updated.DurationMinutes == previousDurationMinutes)
            return;

        var taskContext = await GetTaskActivityContextAsync(updatedTaskId);
        await taskActivityService.RecordUserTaskEventAsync(
            TaskActivityEventType.TaskTimeChanged,
            userId,
            updatedTaskId,
            taskContext.SpaceId,
            taskContext.ProjectId,
            new
                {
                calendar_block_id = updated.Id,
                previous_start_at = previousStartAt,
                new_start_at = updated.StartAt,
                previous_duration_minutes = previousDurationMinutes,
                new_duration_minutes = updated.DurationMinutes,
                delta_minutes = (int)(updated.StartAt - previousStartAt).TotalMinutes,
                duration_delta_minutes = updated.DurationMinutes - previousDurationMinutes
                });
    }

    private async Task<(Guid? ProjectId, Guid? SpaceId)> GetTaskActivityContextAsync(Guid taskId)
    {
        var task = await taskRepository.GetByIdAsync(taskId);
        if (task is null) return (null, null);

        var spaceId = await taskRepository.GetSpaceIdForTaskAsync(task);
        return (task.ProjectId, spaceId);
    }

    private static string? NormalizeTitle(string? title)
    {
        var normalized = title?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private async Task NotifySafelyAsync(Func<Task> publish, string eventName, Guid blockId)
    {
        try
        {
            await publish();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR publish failed for event {EventName} and calendar block {BlockId}.", eventName, blockId);
        }
    }
}
