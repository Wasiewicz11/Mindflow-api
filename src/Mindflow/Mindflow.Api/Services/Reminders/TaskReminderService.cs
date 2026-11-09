using Microsoft.EntityFrameworkCore;
using Npgsql;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Services.Reminders;

public interface ITaskReminderService
{
    Task<int> SendDueAsync(DateTimeOffset now, CancellationToken ct = default);
}

public class TaskReminderService(
    MindflowDbContext db,
    IReminderDispatcher dispatcher,
    IReminderActionTokens tokens,
    IConfiguration configuration,
    ILogger<TaskReminderService> logger) : ITaskReminderService
{
    private static readonly TimeSpan TokenLifetimeAfterDue = TimeSpan.FromDays(2);

    public async Task<int> SendDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var from = today.AddDays(-2);
        var to = today.AddDays(8);
        var snoozeFrom = now - ReminderSchedule.Grace;
        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(task => !task.IsCompleted
                           && task.Status != TaskStatus.Completed
                           && !task.ReminderDisabled
                           && task.RecurringTaskId == null
                           && task.Recurrence == null
                           && task.DueDate.HasValue
                           && task.DueTime.HasValue
                           && ((task.DueDate.Value >= from && task.DueDate.Value <= to)
                               || (task.ReminderSnoozedUntil != null && task.ReminderSnoozedUntil >= snoozeFrom)))
            .ToListAsync(ct);
        if (tasks.Count == 0) return 0;

        var userIds = tasks.Select(task => task.UserId).Distinct().ToArray();
        var timeZones = await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.TimeZone, ct);
        var settingsByUser = await db.NotificationSettings.AsNoTracking()
            .Where(settings => userIds.Contains(settings.UserId))
            .ToDictionaryAsync(settings => settings.UserId, ct);

        var sent = 0;
        foreach (var task in tasks)
        {
            var settings = settingsByUser.GetValueOrDefault(task.UserId) ?? new NotificationSettings();
            var timeZone = ResolveTimeZone(timeZones.GetValueOrDefault(task.UserId));
            var due = ReminderSchedule.FindDue(task, settings, timeZone, now);
            if (due is null) continue;

            var alreadySent = await db.PushNotificationDeliveries.AsNoTracking()
                .AnyAsync(delivery => delivery.UserId == task.UserId && delivery.DeliveryKey == due.DeliveryKey, ct);
            if (alreadySent) continue;

            var text = ReminderText.Build(task.Content, due, timeZone, now);
            var expiresAt = (due.DueAt > now ? due.DueAt : now) + TokenLifetimeAfterDue;
            var message = new ReminderMessage(
                Guid.NewGuid(),
                task.UserId,
                task.Id,
                text.Title,
                text.Body,
                text.LocalDue,
                due.DueAt,
                ReminderLinks.TaskUrl(configuration, task.Id),
                tokens.Create(task.Id, task.UserId, expiresAt));

            var delivered = await dispatcher.DispatchAsync(message, ct);
            if (delivered.Count == 0) continue;

            if (await RecordDeliveryAsync(task.UserId, due.DeliveryKey, now, ct)) sent++;
        }

        return sent;
    }

    private async Task<bool> RecordDeliveryAsync(Guid userId, string deliveryKey, DateTimeOffset now, CancellationToken ct)
    {
        var delivery = new PushNotificationDelivery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeliveryKey = deliveryKey,
            SentAt = now
        };
        db.PushNotificationDeliveries.Add(delivery);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception)
        {
            db.Entry(delivery).State = EntityState.Detached;
            if (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) return false;
            logger.LogWarning(exception, "Recording reminder delivery {DeliveryKey} failed.", deliveryKey);
            return false;
        }
    }

    private TimeZoneInfo ResolveTimeZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Unknown time zone {TimeZone}; using UTC for reminders.", timeZone);
            return TimeZoneInfo.Utc;
        }
    }
}
