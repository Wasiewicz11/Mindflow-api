using Mindflow.Api.Models;
using Mindflow.Api.Services.Reminders;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderScheduleTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly NotificationSettings Defaults = new();

    private static TaskItem Task(string date = "2030-01-10", string time = "14:00", int? minutes = null) => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Content = "Telefon",
        DueDate = DateOnly.Parse(date),
        DueTime = TimeOnly.Parse(time),
        ReminderMinutesBefore = minutes
    };

    private static DateTimeOffset WarsawTime(string value) =>
        new(DateTime.Parse(value), Warsaw.GetUtcOffset(DateTime.Parse(value)));

    [Fact]
    public void Default_reminder_fires_ten_minutes_before_within_grace()
    {
        Assert.Null(ReminderSchedule.FindDue(Task(), Defaults, Warsaw, WarsawTime("2030-01-10 13:49")));
        var due = ReminderSchedule.FindDue(Task(), Defaults, Warsaw, WarsawTime("2030-01-10 13:50"));
        Assert.NotNull(due);
        Assert.Equal(10, due.MinutesBefore);
        Assert.NotNull(ReminderSchedule.FindDue(Task(), Defaults, Warsaw, WarsawTime("2030-01-10 14:04")));
        Assert.Null(ReminderSchedule.FindDue(Task(), Defaults, Warsaw, WarsawTime("2030-01-10 14:05")));
    }

    [Fact]
    public void Own_zero_minutes_fires_at_due_time()
    {
        var due = ReminderSchedule.FindDue(Task(minutes: 0), Defaults, Warsaw, WarsawTime("2030-01-10 14:00"));
        Assert.Equal(WarsawTime("2030-01-10 14:00"), due!.RemindAt);
    }

    [Fact]
    public void Own_minutes_ignore_disabled_default()
    {
        var settings = new NotificationSettings { BlockRemindersEnabled = false };
        Assert.Null(ReminderSchedule.FindDue(Task(), settings, Warsaw, WarsawTime("2030-01-10 13:50")));
        Assert.NotNull(ReminderSchedule.FindDue(Task(minutes: 60), settings, Warsaw, WarsawTime("2030-01-10 13:00")));
    }

    [Fact]
    public void Disabled_completed_or_globally_off_never_fire()
    {
        var now = WarsawTime("2030-01-10 13:50");
        var disabled = Task(); disabled.ReminderDisabled = true;
        var completed = Task(); completed.Status = TaskStatus.Completed; completed.IsCompleted = true;
        Assert.Null(ReminderSchedule.FindDue(disabled, Defaults, Warsaw, now));
        Assert.Null(ReminderSchedule.FindDue(completed, Defaults, Warsaw, now));
        Assert.Null(ReminderSchedule.FindDue(Task(), new NotificationSettings { Enabled = false }, Warsaw, now));
    }

    [Fact]
    public void Snooze_replaces_original_moment_and_changes_key()
    {
        var task = Task();
        task.ReminderSnoozedUntil = WarsawTime("2030-01-10 14:10");
        Assert.Null(ReminderSchedule.FindDue(task, Defaults, Warsaw, WarsawTime("2030-01-10 13:50")));
        var due = ReminderSchedule.FindDue(task, Defaults, Warsaw, WarsawTime("2030-01-10 14:11"));
        Assert.True(due!.IsSnoozed);
        Assert.Contains(":snooze:", due.DeliveryKey);
    }

    [Fact]
    public void Delivery_key_changes_with_minutes()
    {
        var a = ReminderSchedule.FindDue(Task(minutes: 10), Defaults, Warsaw, WarsawTime("2030-01-10 13:50"));
        var b = ReminderSchedule.FindDue(Task(minutes: 15), Defaults, Warsaw, WarsawTime("2030-01-10 13:45"));
        Assert.NotEqual(a!.DeliveryKey, b!.DeliveryKey);
    }

    [Fact]
    public void Nonexistent_local_time_on_spring_forward_is_skipped()
    {
        Assert.Null(LocalTime.ToUtc(new DateOnly(2030, 3, 31), new TimeOnly(2, 30), Warsaw));
        Assert.NotNull(LocalTime.ToUtc(new DateOnly(2030, 10, 27), new TimeOnly(2, 30), Warsaw));
    }
}
