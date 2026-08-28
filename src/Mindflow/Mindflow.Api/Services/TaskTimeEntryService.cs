using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Task = System.Threading.Tasks.Task;

namespace Mindflow.Api.Services;

public class TaskTimeEntryService(
    ITaskTimeEntryRepository timeEntryRepository,
    ITaskRepository taskRepository,
    ICurrentUserService currentUserService,
    IAccessService accessService,
    ITaskActivityService taskActivityService) : ITaskTimeEntryService
{
    public async Task<IEnumerable<TaskTimeEntryResponse>> GetAsync(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new BadRequestException("The 'from' date cannot be later than the 'to' date.");

        var userId = await currentUserService.GetCurrentUserIdAsync();
        var entries = await timeEntryRepository.GetForUserInRangeAsync(userId, from, to);
        return entries.Select(ToResponse);
    }

    public async Task<IEnumerable<TaskTimeEntryResponse>?> GetForTaskAsync(Guid taskId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        if (!await accessService.CanAccessTaskAsync(taskId, userId))
            return null;

        var entries = await timeEntryRepository.GetForUserTaskAsync(userId, taskId);
        return entries.Select(ToResponse);
    }

    public async Task<TaskTimeEntryMutationResponse?> CreateAsync(Guid taskId, CreateTaskTimeEntryRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        if (!await accessService.CanAccessTaskAsync(taskId, userId))
            return null;

        var task = await taskRepository.GetByIdAsync(taskId);
        if (task is null) return null;

        if (request.ClearEstimatedHours)
            task.EstimatedHours = null;
        else if (request.EstimatedHours.HasValue)
            task.EstimatedHours = request.EstimatedHours;

        var now = DateTimeOffset.UtcNow;
        var entry = BuildEntry(userId, task, request, now, requireTime: true);
        var created = await timeEntryRepository.CreateAsync(entry);
        await RecordTimeSetActivityAsync(userId, task, created);
        var loggedMinutes = await timeEntryRepository.GetDurationMinutesForTaskAsync(userId, task.Id);
        return new TaskTimeEntryMutationResponse(ToResponse(created), TaskResponseMapper.ToDetailResponse(task, loggedMinutes));
    }

    public async Task<TaskTimeEntryResponse?> CreateStandaloneAsync(CreateStandaloneTimeEntryRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var content = request.Content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            throw new BadRequestException("Time entry content is required.");

        if (request.ProjectId.HasValue && !await accessService.CanAccessProjectAsync(request.ProjectId.Value, userId))
            return null;

        var now = DateTimeOffset.UtcNow;
        var normalized = NormalizeTimeInput(
            new CreateTaskTimeEntryRequest(
                request.WorkDate,
                request.DurationMinutes,
                request.StartAt,
                request.EndAt,
                null,
                false),
            requireTime: true);

        var entry = new TaskTimeEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TaskId = null,
            ProjectId = request.ProjectId,
            TaskContent = content,
            TaskPriority = TaskPriority.P4,
            TaskStatus = Mindflow.Api.Models.Enums.TaskStatus.NotStarted,
            Tags = new List<string>(),
            WorkDate = normalized.WorkDate,
            DurationMinutes = normalized.DurationMinutes,
            StartAt = normalized.StartAt,
            EndAt = normalized.EndAt,
            EstimatedHours = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        var created = await timeEntryRepository.CreateAsync(entry);
        return ToResponse(created);
    }

    public async Task<UpdateTaskTimeEntryResponse?> UpdateAsync(Guid id, UpdateTaskTimeEntryRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var entry = await timeEntryRepository.GetByIdAsync(id);

        if (entry is null || entry.UserId != userId)
            return null;

        TaskItem? task = null;
        Guid? spaceId = null;
        if (entry.TaskId is Guid taskId)
        {
            task = await taskRepository.GetByIdAsync(taskId);
            if (task is not null)
                spaceId = await taskRepository.GetSpaceIdForTaskAsync(task);
        }

        var previousWorkDate = entry.WorkDate;
        var previousStartAt = entry.StartAt;
        var previousEndAt = entry.EndAt;
        var previousDurationMinutes = entry.DurationMinutes;
        var previousEstimatedHours = entry.EstimatedHours;

        var hasTimingInput = request.WorkDate.HasValue
            || request.DurationMinutes.HasValue
            || request.StartAt.HasValue
            || request.EndAt.HasValue;

        var normalized = hasTimingInput
            ? NormalizeTimeInput(
                new CreateTaskTimeEntryRequest(
                    request.WorkDate ?? entry.WorkDate,
                    request.DurationMinutes ?? (!request.StartAt.HasValue && !request.EndAt.HasValue ? entry.DurationMinutes : null),
                    request.StartAt,
                    request.EndAt,
                    request.EstimatedHours,
                    request.ClearEstimatedHours),
                requireTime: true)
            : new NormalizedTimeInput(entry.WorkDate, entry.DurationMinutes, entry.StartAt, entry.EndAt);

        entry.WorkDate = normalized.WorkDate;
        entry.DurationMinutes = normalized.DurationMinutes;
        entry.StartAt = normalized.StartAt;
        entry.EndAt = normalized.EndAt;

        if (request.ClearEstimatedHours)
        {
            entry.EstimatedHours = null;
            if (task is not null) task.EstimatedHours = null;
        }
        else if (request.EstimatedHours.HasValue)
        {
            entry.EstimatedHours = request.EstimatedHours;
            if (task is not null) task.EstimatedHours = request.EstimatedHours;
        }

        entry.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await timeEntryRepository.UpdateAsync(entry);

        if (entry.TaskId is Guid eventTaskId)
        {
            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskTimeSet,
                userId,
                eventTaskId,
                spaceId,
                entry.ProjectId,
                new
                {
                    time_entry_id = entry.Id,
                    previous_work_date = previousWorkDate,
                    work_date = entry.WorkDate,
                    previous_start_at = previousStartAt,
                    start_at = entry.StartAt,
                    previous_end_at = previousEndAt,
                    end_at = entry.EndAt,
                    previous_duration_minutes = previousDurationMinutes,
                    duration_minutes = entry.DurationMinutes,
                    previous_estimated_hours = previousEstimatedHours,
                    estimated_hours = entry.EstimatedHours
                });
        }

        TaskDetailResponse? taskResponse = null;
        if (task is not null)
        {
            var loggedMinutes = await timeEntryRepository.GetDurationMinutesForTaskAsync(userId, task.Id);
            taskResponse = TaskResponseMapper.ToDetailResponse(task, loggedMinutes);
        }

        return new UpdateTaskTimeEntryResponse(ToResponse(updated), taskResponse);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var entry = await timeEntryRepository.GetByIdAsync(id);

        if (entry is null || entry.UserId != userId)
            return false;

        var deleted = await timeEntryRepository.DeleteAsync(entry);
        if (!deleted) return false;

        if (entry.TaskId is Guid taskId)
        {
            await taskActivityService.RecordUserTaskEventAsync(
                TaskActivityEventType.TaskTimeRemoved,
                userId,
                taskId,
                null,
                entry.ProjectId,
                new
                {
                    time_entry_id = entry.Id,
                    work_date = entry.WorkDate,
                    previous_start_at = entry.StartAt,
                    previous_end_at = entry.EndAt,
                    previous_duration_minutes = entry.DurationMinutes
                });
        }

        return true;
    }

    public TaskTimeEntry BuildEntry(Guid userId, TaskItem task, CreateTaskTimeEntryRequest request, DateTimeOffset now, bool requireTime)
    {
        var normalized = NormalizeTimeInput(request, requireTime);

        return new TaskTimeEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TaskId = task.Id,
            ProjectId = task.ProjectId,
            TaskContent = task.Content,
            TaskPriority = task.Priority,
            TaskStatus = task.Status,
            Tags = task.Tags.ToList(),
            WorkDate = normalized.WorkDate,
            DurationMinutes = normalized.DurationMinutes,
            StartAt = normalized.StartAt,
            EndAt = normalized.EndAt,
            EstimatedHours = request.EstimatedHours,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public TaskTimeEntryResponse ToResponse(TaskTimeEntry entry) =>
        new(
            entry.Id,
            entry.UserId,
            entry.TaskId,
            entry.ProjectId,
            entry.TaskContent,
            entry.TaskPriority,
            entry.TaskStatus,
            entry.Tags.ToArray(),
            entry.WorkDate,
            entry.DurationMinutes,
            entry.StartAt,
            entry.EndAt,
            entry.EstimatedHours,
            entry.CreatedAt,
            entry.UpdatedAt);

    private async Task RecordTimeSetActivityAsync(Guid userId, TaskItem task, TaskTimeEntry entry)
    {
        var spaceId = await taskRepository.GetSpaceIdForTaskAsync(task);
        await taskActivityService.RecordUserTaskEventAsync(
            TaskActivityEventType.TaskTimeSet,
            userId,
            task.Id,
            spaceId,
            task.ProjectId,
            new
            {
                time_entry_id = entry.Id,
                work_date = entry.WorkDate,
                start_at = entry.StartAt,
                end_at = entry.EndAt,
                duration_minutes = entry.DurationMinutes,
                estimated_hours = entry.EstimatedHours
            });
    }

    private static NormalizedTimeInput NormalizeTimeInput(CreateTaskTimeEntryRequest request, bool requireTime)
    {
        if (request.StartAt.HasValue != request.EndAt.HasValue)
            throw new BadRequestException("Both start and end time must be provided together.");

        var startAt = request.StartAt?.ToUniversalTime();
        var endAt = request.EndAt?.ToUniversalTime();
        int? durationMinutes = request.DurationMinutes;

        if (startAt.HasValue && endAt.HasValue)
        {
            if (endAt.Value <= startAt.Value)
                throw new BadRequestException("End time must be later than start time.");

            var derivedDuration = (int)Math.Round((endAt.Value - startAt.Value).TotalMinutes);
            if (derivedDuration is < 1 or > 1440)
                throw new BadRequestException("Work time must be between 1 minute and 24 hours.");

            durationMinutes = derivedDuration;
        }

        if (!durationMinutes.HasValue)
        {
            if (requireTime)
                throw new BadRequestException("Work time is required.");

            durationMinutes = 0;
        }

        if (durationMinutes.Value is < 0 or > 1440)
            throw new BadRequestException("Work time must be between 1 minute and 24 hours.");

        if (requireTime && durationMinutes.Value == 0)
            throw new BadRequestException("Work time is required.");

        var workDate = request.WorkDate
            ?? (request.StartAt.HasValue
                ? DateOnly.FromDateTime(request.StartAt.Value.DateTime)
                : DateOnly.FromDateTime(DateTime.UtcNow));

        return new NormalizedTimeInput(workDate, durationMinutes.Value, startAt, endAt);
    }

    private record NormalizedTimeInput(DateOnly WorkDate, int DurationMinutes, DateTimeOffset? StartAt, DateTimeOffset? EndAt);
}
