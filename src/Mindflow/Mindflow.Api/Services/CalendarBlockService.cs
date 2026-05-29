using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Task = System.Threading.Tasks.Task;

namespace Mindflow.Api.Services;

public class CalendarBlockService(
    ICalendarBlockRepository calendarBlockRepository,
    ICurrentUserService currentUserService,
    IAccessService accessService,
    ITasksNotifier notifier,
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
        if (!await accessService.CanAccessTaskAsync(request.TaskId, userId))
            return null;

        var now = DateTimeOffset.UtcNow;
        var block = new CalendarBlock
        {
            Id = Guid.NewGuid(),
            TaskId = request.TaskId,
            UserId = userId,
            StartAt = request.StartAt.ToUniversalTime(),
            DurationMinutes = request.DurationMinutes,
            CreatedAt = now,
            UpdatedAt = now,
            Provider = CalendarBlockProvider.Local,
            SyncStatus = CalendarBlockSyncStatus.Local
        };

        var created = await calendarBlockRepository.CreateAsync(block);
        var response = ToResponse(created);

        await NotifySafelyAsync(
            () => notifier.CalendarBlockCreatedAsync(response),
            "CalendarBlockCreated",
            created.Id);

        return response;
    }

    public async Task<CalendarBlockResponse?> UpdateAsync(Guid id, UpdateCalendarBlockRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var block = await calendarBlockRepository.GetByIdAsync(id);

        if (block is null || block.UserId != userId)
            throw new NotFoundException($"Calendar block with id {id} not found");

        if (!await accessService.CanAccessTaskAsync(request.TaskId, userId))
            return null;

        block.TaskId = request.TaskId;
        block.StartAt = request.StartAt.ToUniversalTime();
        block.DurationMinutes = request.DurationMinutes;
        block.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await calendarBlockRepository.UpdateAsync(block);
        var response = ToResponse(updated);

        await NotifySafelyAsync(
            () => notifier.CalendarBlockUpdatedAsync(response),
            "CalendarBlockUpdated",
            updated.Id);

        return response;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var block = await calendarBlockRepository.GetByIdAsync(id);

        if (block is null || block.UserId != userId)
            return false;

        var deleted = await calendarBlockRepository.DeleteAsync(block);
        if (!deleted) return false;

        await NotifySafelyAsync(
            () => notifier.CalendarBlockDeletedAsync(block.Id, block.UserId),
            "CalendarBlockDeleted",
            block.Id);

        return true;
    }

    private static CalendarBlockResponse ToResponse(CalendarBlock block) =>
        new(
            block.Id,
            block.TaskId,
            block.UserId,
            block.StartAt,
            block.DurationMinutes,
            block.CreatedAt,
            block.UpdatedAt,
            block.Provider,
            block.ExternalEventId,
            block.GoogleCalendarId,
            block.SyncStatus);

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
