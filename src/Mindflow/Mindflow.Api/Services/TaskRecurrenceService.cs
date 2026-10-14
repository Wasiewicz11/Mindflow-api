using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Services;

/// <summary>
/// Keeps recurrence evaluation deterministic and idempotent. Past occurrences are stored as
/// regular tasks, while future occurrences are calculated by <see cref="GetUpcomingAsync"/>
/// so a calendar can show them before their scheduled date without prematurely creating work.
/// </summary>
public class TaskRecurrenceService(
    MindflowDbContext db,
    IAccessService accessService,
    ITaskTimeEntryRepository timeEntryRepository) : ITaskRecurrenceService
{
    private const int MaxPreviewDays = 366;

    public async Task<TaskDetailResponse?> UpdateAsync(
        Guid userId,
        Guid taskId,
        UpdateTaskRecurrenceRequest request,
        CancellationToken ct = default)
    {
        if (!await accessService.CanAccessTaskAsync(taskId, userId)) return null;
        Validate(request);

        var task = await db.Tasks
            .Include(item => item.Subtasks)
            .Include(item => item.Recurrence)
            .FirstOrDefaultAsync(item => item.Id == taskId, ct);
        if (task is null || task.RecurringTaskId.HasValue) return null;

        var now = DateTimeOffset.UtcNow;
        var recurrence = task.Recurrence ?? new TaskRecurrence
        {
            TaskId = task.Id,
            CreatedAt = now
        };

        recurrence.IsEnabled = request.IsEnabled;
        recurrence.Frequency = request.Frequency;
        recurrence.Interval = request.Interval;
        recurrence.StartDate = request.StartDate;
        recurrence.EndDate = request.EndDate;
        recurrence.DaysOfWeek = NormalizeWeekdays(request.DaysOfWeek, request.Frequency, request.StartDate);
        recurrence.DayOfMonth = request.Frequency is TaskRecurrenceFrequency.Monthly or TaskRecurrenceFrequency.Yearly
            ? request.DayOfMonth ?? request.StartDate.Day
            : null;
        recurrence.MonthOfYear = request.Frequency == TaskRecurrenceFrequency.Yearly
            ? request.MonthOfYear ?? request.StartDate.Month
            : null;
        recurrence.DueTime = request.DueTime;
        recurrence.NotificationsEnabled = request.NotificationsEnabled;
        recurrence.ReminderMinutesBefore = request.ReminderMinutesBefore;
        recurrence.NotificationWindowStart = request.NotificationWindowStart;
        recurrence.NotificationWindowEnd = request.NotificationWindowEnd;
        recurrence.UpdatedAt = now;

        if (task.Recurrence is null)
        {
            task.Recurrence = recurrence;
            db.TaskRecurrences.Add(recurrence);
        }

        // The source remains a useful canonical record, but it is rendered separately as a
        // recurring template instead of being treated as an ordinary dated task.
        task.DueDate = request.StartDate;
        task.DueTime = request.DueTime;
        await db.SaveChangesAsync(ct);

        if (recurrence.IsEnabled)
            await MaterializeForRecurrenceAsync(task, recurrence, await GetLocalTodayAsync(task.UserId, ct), ct);

        var loggedMinutes = await timeEntryRepository.GetDurationMinutesForTaskAsync(userId, task.Id);
        var subtaskMinutes = await timeEntryRepository.GetDurationMinutesBySubtaskAsync(userId, task.Id);
        return TaskResponseMapper.ToDetailResponse(task, loggedMinutes, subtaskMinutes);
    }

    public async Task<TaskDetailResponse?> DisableAsync(Guid userId, Guid taskId, CancellationToken ct = default)
    {
        if (!await accessService.CanAccessTaskAsync(taskId, userId)) return null;
        var task = await db.Tasks
            .Include(item => item.Subtasks)
            .Include(item => item.Recurrence)
            .FirstOrDefaultAsync(item => item.Id == taskId, ct);
        if (task?.Recurrence is null) return null;

        task.Recurrence.IsEnabled = false;
        task.Recurrence.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var loggedMinutes = await timeEntryRepository.GetDurationMinutesForTaskAsync(userId, task.Id);
        var subtaskMinutes = await timeEntryRepository.GetDurationMinutesBySubtaskAsync(userId, task.Id);
        return TaskResponseMapper.ToDetailResponse(task, loggedMinutes, subtaskMinutes);
    }

    public async Task<IReadOnlyList<RecurringTaskPreviewResponse>> GetUpcomingAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > MaxPreviewDays)
            throw new ArgumentException($"The recurrence preview range must be between 0 and {MaxPreviewDays} days.");

        await MaterializePastDueForUserAsync(userId, ct);

        var templates = await db.Tasks
            .AsNoTracking()
            .Include(task => task.Recurrence)
            .Where(task => task.UserId == userId
                           && task.Recurrence != null
                           && task.Recurrence.IsEnabled)
            .ToListAsync(ct);
        if (templates.Count == 0) return Array.Empty<RecurringTaskPreviewResponse>();

        var templateIds = templates.Select(task => task.Id).ToArray();
        var existing = await db.Tasks
            .AsNoTracking()
            .Where(task => task.RecurringTaskId.HasValue
                           && templateIds.Contains(task.RecurringTaskId.Value)
                           && task.RecurrenceOccurrenceDate.HasValue
                           && task.RecurrenceOccurrenceDate.Value >= from
                           && task.RecurrenceOccurrenceDate.Value <= to)
            .Select(task => new { task.RecurringTaskId, task.RecurrenceOccurrenceDate })
            .ToListAsync(ct);
        var materialized = existing
            .Where(item => item.RecurringTaskId.HasValue && item.RecurrenceOccurrenceDate.HasValue)
            .Select(item => (item.RecurringTaskId!.Value, item.RecurrenceOccurrenceDate!.Value))
            .ToHashSet();

        return templates
            .SelectMany(template => GetOccurrenceDates(template.Recurrence!, from, to)
                .Where(date => !materialized.Contains((template.Id, date)))
                .Select(date => new RecurringTaskPreviewResponse(
                    template.Id,
                    date,
                    template.Content,
                    template.Priority,
                    template.ProjectId,
                    template.Recurrence!.DueTime)))
            .OrderBy(item => item.OccurrenceDate)
            .ThenBy(item => item.DueTime)
            .ToArray();
    }

    public async Task<int> MaterializePastDueForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var today = await GetLocalTodayAsync(userId, ct);
        var templates = await db.Tasks
            .AsNoTracking()
            .Include(task => task.Subtasks)
            .Include(task => task.Recurrence)
            .Where(task => task.UserId == userId
                           && task.Recurrence != null
                           && task.Recurrence.IsEnabled
                           && task.Recurrence.StartDate <= today)
            .ToListAsync(ct);

        var created = 0;
        foreach (var template in templates)
            created += await MaterializeForRecurrenceAsync(template, template.Recurrence!, today, ct);
        return created;
    }

    public async Task<int> MaterializePastDueOccurrencesAsync(CancellationToken ct = default)
    {
        var userIds = await db.Tasks
            .AsNoTracking()
            .Where(task => task.Recurrence != null && task.Recurrence.IsEnabled)
            .Select(task => task.UserId)
            .Distinct()
            .ToArrayAsync(ct);

        var created = 0;
        foreach (var userId in userIds)
            created += await MaterializePastDueForUserAsync(userId, ct);
        return created;
    }

    private async Task<int> MaterializeForRecurrenceAsync(
        TaskItem template,
        TaskRecurrence recurrence,
        DateOnly localToday,
        CancellationToken ct)
    {
        if (!recurrence.IsEnabled || recurrence.StartDate > localToday) return 0;
        var end = recurrence.EndDate is { } recurrenceEnd && recurrenceEnd < localToday
            ? recurrenceEnd
            : localToday;
        if (end < recurrence.StartDate) return 0;

        var existingDates = await db.Tasks
            .AsNoTracking()
            .Where(task => task.RecurringTaskId == template.Id
                           && task.RecurrenceOccurrenceDate.HasValue
                           && task.RecurrenceOccurrenceDate.Value >= recurrence.StartDate
                           && task.RecurrenceOccurrenceDate.Value <= end)
            .Select(task => task.RecurrenceOccurrenceDate!.Value)
            .ToListAsync(ct);
        var existing = existingDates.ToHashSet();
        var created = 0;

        foreach (var occurrenceDate in GetOccurrenceDates(recurrence, recurrence.StartDate, end))
        {
            if (existing.Contains(occurrenceDate)) continue;
            var occurrence = CreateOccurrence(template, recurrence, occurrenceDate);
            db.Tasks.Add(occurrence);
            try
            {
                // Saving one occurrence at a time makes the unique key an idempotency guard
                // even if an app restart or two worker instances overlap.
                await db.SaveChangesAsync(ct);
                created++;
            }
            catch (DbUpdateException)
            {
                db.Entry(occurrence).State = EntityState.Detached;
            }
        }

        return created;
    }

    private static TaskItem CreateOccurrence(TaskItem template, TaskRecurrence recurrence, DateOnly occurrenceDate) => new()
    {
        Id = Guid.NewGuid(),
        UserId = template.UserId,
        ProjectId = template.ProjectId,
        Content = template.Content,
        Description = template.Description,
        IsCompleted = false,
        Priority = template.Priority,
        Status = TaskStatus.NotStarted,
        DueDate = occurrenceDate,
        DueTime = recurrence.DueTime,
        EstimatedHours = template.EstimatedHours,
        Tags = template.Tags.ToList(),
        Subtasks = template.Subtasks
            .OrderBy(subtask => subtask.SortOrder)
            .Select((subtask, sortOrder) => new TaskSubtask
            {
                Id = Guid.NewGuid(),
                Content = subtask.Content,
                Description = subtask.Description,
                IsCompleted = false,
                Status = TaskStatus.NotStarted,
                EstimatedHours = subtask.EstimatedHours,
                DueDate = GetOccurrenceSubtaskDate(template, subtask, occurrenceDate),
                SortOrder = sortOrder,
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList(),
        RecurringTaskId = template.Id,
        RecurrenceOccurrenceDate = occurrenceDate,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static DateOnly? GetOccurrenceSubtaskDate(TaskItem template, TaskSubtask subtask, DateOnly occurrenceDate)
    {
        if (!subtask.DueDate.HasValue) return null;
        return template.DueDate.HasValue
            ? occurrenceDate.AddDays(subtask.DueDate.Value.DayNumber - template.DueDate.Value.DayNumber)
            : occurrenceDate;
    }

    internal static IEnumerable<DateOnly> GetOccurrenceDates(TaskRecurrence recurrence, DateOnly from, DateOnly to)
    {
        var start = from < recurrence.StartDate ? recurrence.StartDate : from;
        var end = recurrence.EndDate is { } recurrenceEnd && recurrenceEnd < to ? recurrenceEnd : to;
        if (end < start) yield break;

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (MatchesOccurrence(recurrence, date)) yield return date;
        }
    }

    private static bool MatchesOccurrence(TaskRecurrence recurrence, DateOnly date)
    {
        var daysSinceStart = date.DayNumber - recurrence.StartDate.DayNumber;
        if (daysSinceStart < 0) return false;

        return recurrence.Frequency switch
        {
            TaskRecurrenceFrequency.Daily => daysSinceStart % recurrence.Interval == 0,
            TaskRecurrenceFrequency.Weekly => daysSinceStart / 7 % recurrence.Interval == 0
                                             && recurrence.DaysOfWeek.Contains((int)date.DayOfWeek),
            TaskRecurrenceFrequency.Monthly => MonthsBetween(recurrence.StartDate, date) % recurrence.Interval == 0
                                               && date.Day == Math.Min(recurrence.DayOfMonth ?? recurrence.StartDate.Day,
                                                   DateTime.DaysInMonth(date.Year, date.Month)),
            TaskRecurrenceFrequency.Yearly => date.Month == (recurrence.MonthOfYear ?? recurrence.StartDate.Month)
                                              && date.Year >= recurrence.StartDate.Year
                                              && (date.Year - recurrence.StartDate.Year) % recurrence.Interval == 0
                                              && date.Day == Math.Min(recurrence.DayOfMonth ?? recurrence.StartDate.Day,
                                                  DateTime.DaysInMonth(date.Year, date.Month)),
            _ => false
        };
    }

    private static int MonthsBetween(DateOnly start, DateOnly date) => (date.Year - start.Year) * 12 + date.Month - start.Month;

    private static List<int> NormalizeWeekdays(
        IReadOnlyCollection<int>? values,
        TaskRecurrenceFrequency frequency,
        DateOnly startDate)
    {
        if (frequency != TaskRecurrenceFrequency.Weekly) return new List<int>();
        var normalized = values?.Where(day => day is >= 0 and <= 6).Distinct().Order().ToList() ?? new List<int>();
        return normalized.Count > 0 ? normalized : new List<int> { (int)startDate.DayOfWeek };
    }

    private static void Validate(UpdateTaskRecurrenceRequest request)
    {
        if (request.EndDate is { } end && end < request.StartDate)
            throw new ArgumentException("The recurrence end date cannot be before its start date.");
        if (request.DaysOfWeek?.Any(day => day is < 0 or > 6) == true)
            throw new ArgumentException("DaysOfWeek must contain values from 0 (Sunday) to 6 (Saturday).");
        if (request.NotificationWindowStart.HasValue != request.NotificationWindowEnd.HasValue)
            throw new ArgumentException("Both notification window times must be provided together.");
        if (request.Frequency == TaskRecurrenceFrequency.Yearly
            && request.MonthOfYear is not null
            && request.DayOfMonth is not null
            && request.DayOfMonth > DateTime.DaysInMonth(2024, request.MonthOfYear.Value))
            throw new ArgumentException("The selected day does not exist in the selected month.");
    }

    private async Task<DateOnly> GetLocalTodayAsync(Guid userId, CancellationToken ct)
    {
        var timeZoneId = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.TimeZone)
            .FirstOrDefaultAsync(ct);
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(timeZoneId ?? "UTC")).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }
}
