using Mindflow.Api.Models;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Services.Reminders;

public record DueReminder(DateTimeOffset DueAt, DateTimeOffset RemindAt, int MinutesBefore, bool IsSnoozed, string DeliveryKey);

public static class ReminderSchedule
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    public static int? ResolveMinutesBefore(TaskItem task, NotificationSettings settings)
    {
        if (task.ReminderDisabled) return null;
        if (task.ReminderMinutesBefore is int own) return own;
        return settings.BlockRemindersEnabled ? settings.BlockReminderMinutes : null;
    }

    public static DueReminder? FindDue(TaskItem task, NotificationSettings settings, TimeZoneInfo timeZone, DateTimeOffset now)
    {
        if (!settings.Enabled || task.ReminderDisabled) return null;
        if (task.IsCompleted || task.Status == TaskStatus.Completed) return null;
        if (task.DueDate is not DateOnly date || task.DueTime is not TimeOnly time) return null;

        var dueAt = LocalTime.ToUtc(date, time, timeZone);
        if (dueAt is null) return null;

        if (task.ReminderSnoozedUntil is DateTimeOffset snoozedUntil)
        {
            return IsInWindow(snoozedUntil, now)
                ? new DueReminder(dueAt.Value, snoozedUntil, task.ReminderMinutesBefore ?? 0, true,
                    $"task:{task.Id:N}:snooze:{snoozedUntil.UtcTicks}")
                : null;
        }

        var minutes = ResolveMinutesBefore(task, settings);
        if (minutes is null) return null;

        var remindAt = dueAt.Value.AddMinutes(-minutes.Value);
        return IsInWindow(remindAt, now)
            ? new DueReminder(dueAt.Value, remindAt, minutes.Value, false,
                $"task:{task.Id:N}:{date:yyyy-MM-dd}:{time:HH\\:mm}:r{minutes.Value}")
            : null;
    }

    private static bool IsInWindow(DateTimeOffset moment, DateTimeOffset now) =>
        moment <= now && now < moment + Grace;
}
