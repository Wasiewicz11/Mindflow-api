using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

/// <summary>
/// The schedule owned by a task template. Generated tasks reference the template through
/// <see cref="TaskItem.RecurringTaskId"/>; this keeps a completed occurrence independent
/// from subsequent occurrences.
/// </summary>
public class TaskRecurrence
{
    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public TaskRecurrenceFrequency Frequency { get; set; }
    public int Interval { get; set; } = 1;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public List<int> DaysOfWeek { get; set; } = new();
    public int? DayOfMonth { get; set; }
    public int? MonthOfYear { get; set; }
    public TimeOnly? DueTime { get; set; }
    public bool NotificationsEnabled { get; set; }
    public int ReminderMinutesBefore { get; set; } = 10;
    public TimeOnly? NotificationWindowStart { get; set; }
    public TimeOnly? NotificationWindowEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
